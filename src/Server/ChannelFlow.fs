module ChannelFlow

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl

open Akkling
open Akkling.Streams

type Message = Message of string with static member Empty = Message null

// message timestamp
type MessageTs = int * System.DateTime

/// Client protocol message (messages sent from channel to client actor)
type 'User ChatClientMessage =
    | ChatMessage of ts: MessageTs * author: 'User * Message
    | Joined of ts: MessageTs * user: 'User * all: 'User seq
    | Left of ts: MessageTs * user: 'User * all: 'User seq

/// Channel actor protocol (server side protocol)
type 'User ChannelMessage =
    | NewParticipant of user: 'User * subscriber: IActorRef<'User ChatClientMessage>
    | ParticipantLeft of 'User
    | NewMessage of 'User * Message
    | ListUsers

module internal Internals =
    // maps user login
    type 'User ChannelParties when 'User: comparison = Map<'User, 'User * IActorRef<'User ChatClientMessage>>
    type 'User ChannelState when 'User: comparison = {
        Parties: ChannelParties<'User>
        LastEventId: int
    }

open Internals
/// Creates channel actor
let createChannel<'User when 'User: comparison> (system: ActorSystem) name =

    let incId chan = { chan with LastEventId = chan.LastEventId + 1}
    let dispatch (parties: 'User ChannelParties) (msg: 'User ChatClientMessage): unit =
        parties |> Map.iter (fun _ (_, subscriber) -> subscriber <! msg)
    let allMembers = Map.toSeq >> Seq.map (snd >> fst)

    let behavior state (ctx: Actor<'User ChannelMessage>): obj -> _ =
        function
        | Terminated (t,_,_) ->
            match state.Parties |> Map.tryFindKey (fun _ (_, ref) -> ref = t) with
            | Some key ->
                {state with Parties = state.Parties |> Map.remove key} |> ignored
            | _ -> ignored state

        | :? ChannelMessage<'User> as channelEvent ->
            let ts = state.LastEventId, System.DateTime.Now

            match channelEvent with
            | NewParticipant (user, subscriber) ->
                do monitor ctx subscriber |> ignore
                let parties = state.Parties |> Map.add user (user, subscriber)
                do dispatch state.Parties <| Joined (ts, user, parties |> allMembers)
                incId { state with Parties = parties}

            | ParticipantLeft user ->
                let parties = state.Parties |> Map.remove user
                do dispatch state.Parties <| Left (ts, user, parties |> allMembers)
                incId { state with Parties = parties}

            | NewMessage (user, message) ->
                if state.Parties |> Map.containsKey user then
                    do dispatch state.Parties <| ChatMessage (ts, user, message)
                incId state

            | ListUsers ->
                let users = state.Parties |> Map.toList |> List.map fst
                ctx.Sender() <! users
                state
            |> ignored

        | _ -> unhandled()

    // TODO * check monitor does work
    in
    props <| actorOf2 (behavior { Parties = Map.empty; LastEventId = 1000 }) |> (spawn system name)

// Creates a Flow instance for user in channel.
// When materialized flow connects user to channel and starts bidirectional communication.
let createChannelFlow<'User> (channelActor: IActorRef<_>) (user: 'User) =
    let chatInSink = Sink.toActorRef (ParticipantLeft user) channelActor

    let fin =
        (Flow.empty<Message, Akka.NotUsed>
            |> Flow.map (fun msg -> NewMessage(user, msg))
        ).To(chatInSink)

    // The counter-part which is a source that will create a target ActorRef per
    // materialization where the chatActor will send its messages to.
    // This source will only buffer one element and will fail if the client doesn't read
    // messages fast enough.
    let fout =
        Source.actorRef OverflowStrategy.Fail 1
        |> Source.mapMaterializedValue (fun (sub: IActorRef<'User ChatClientMessage>) -> channelActor <! NewParticipant (user, sub); Akka.NotUsed.Instance)

    Flow.ofSinkAndSource fin fout


/// User session multiplexer. Creates a flow that receives user messages for multiple channels, binds each stream to channel flow
/// and finally collects the messages from multiple channels into single stream.
/// When materialized return a "connect" function which, given channel and channel flow, adds it to session. "Connect" returns a killswitch to remove the channel.
let createUserSessionFlow<'User, 'Chan when 'Chan: equality>
    (materializer: Akka.Streams.IMaterializer) =

    let inhub = BroadcastHub.Sink<'Chan * Message>(bufferSize = 256)
    let outhub = MergeHub.Source<'Chan * 'User ChatClientMessage>(perProducerBufferSize = 16)

    let sourceTo (sink) (source: Source<'TOut, 'TMat>) = source.To(sink)

    let combine
            (producer: Source<'Chan * Message, Akka.NotUsed>)
            (consumer: Sink<'Chan * 'User ChatClientMessage, Akka.NotUsed>)
            (chanId: 'Chan) (chanFlow: Flow<Message, 'User ChatClientMessage, Akka.NotUsed>) =

        let infilter =
            Flow.empty<'Chan * Message, Akka.NotUsed>
            |> Flow.filter (fst >> (=) chanId)
            |> Flow.map snd

        let graph =
            producer
            |> Source.viaMat (KillSwitches.Single()) Keep.right
            |> Source.via infilter
            |> Source.via chanFlow
            |> Source.map (fun message -> chanId, message)
            |> sourceTo consumer

        graph |> Graph.run materializer

    Flow.ofSinkAndSourceMat inhub combine outhub