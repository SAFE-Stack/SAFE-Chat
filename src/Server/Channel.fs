module Channel

open Akka.Actor
open Akkling
open Akkling.Streams

open Types

/// The message sent out to user
type MessageTs = int * System.DateTime

/// Client protocol message (messages sent from channel to client actor)
type ChatClientMessage =
    | ChatMessage of ts: MessageTs * author: User * Message
    | Joined of ts: MessageTs * user: User * all: User seq
    | Left of ts: MessageTs * user: User * all: User seq

/// Channel actor protocol (server side protocol)
type ChannelMessage =
    | NewParticipant of user: User * subscriber: IActorRef<ChatClientMessage>
    | ParticipantLeft of User
    | NewMessage of User * Message
    | ListUsers

module internal Internals =
    // Channel. Feed of messages for all parties.
    type ChannelParty = User * IActorRef<ChatClientMessage>

    // maps user login
    type ChannelParties = Map<User, ChannelParty>   // FIXME UserId
    type ChannelState = {
        Parties: ChannelParties
        LastEventId: int
    }

open Internals
/// Creates channel actor
let createChannel (system: ActorSystem) name =

    let incId chan = { chan with LastEventId = chan.LastEventId + 1}
    let dispatch (parties: ChannelParties) (msg: ChatClientMessage): unit =
        parties |> Map.iter (fun _ (_, subscriber) -> subscriber <! msg)
    let allMembers = Map.toSeq >> Seq.map (snd >> fst)

    let behavior state (ctx: Actor<ChannelMessage>): obj -> _ =
        function
        | Terminated (t,_,_) ->
            match state.Parties |> Map.tryFindKey (fun _ (_, ref) -> ref = t) with
            | Some key ->
                {state with Parties = state.Parties |> Map.remove key} |> ignored
            | _ -> ignored state

        | :? ChannelMessage as channelEvent ->
            let ts = state.LastEventId, System.DateTime.Now

            match channelEvent with
            | NewParticipant (user, subscriber) ->
                do monitor ctx subscriber |> ignore
                let parties = state.Parties |> Map.add user (user, subscriber)
                do dispatch state.Parties <| ChatClientMessage.Joined (ts, user, parties |> allMembers)
                incId { state with Parties = parties} |> ignored

            | ParticipantLeft user ->
                let parties = state.Parties |> Map.remove user
                do dispatch state.Parties <| ChatClientMessage.Left (ts, user, parties |> allMembers)
                incId { state with Parties = parties} |> ignored

            | NewMessage (user, message) ->
                if state.Parties |> Map.containsKey user then
                    do dispatch state.Parties <| ChatMessage (ts, user, message)
                incId state |> ignored

            | ListUsers ->
                let users = state.Parties |> Map.toList |> List.map fst
                ctx.Sender() <! users
                ignored state

        | _ -> unhandled()

    // TODO * check monitor does work
    in
    props <| actorOf2 (behavior { Parties = Map.empty; LastEventId = 1000 }) |> (spawn system name)

/// Creates a Flow instance for user in channel
let createPartyFlow (channelActor: IActorRef<_>) (user: User) =
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
        Source.actorRef Akka.Streams.OverflowStrategy.Fail 1
        |> Source.mapMaterializedValue (fun (sub: IActorRef<ChatClientMessage>) -> channelActor <! NewParticipant (user, sub); Akka.NotUsed.Instance)

    Flow.ofSinkAndSource fin fout
