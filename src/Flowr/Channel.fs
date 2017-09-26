module fschat.Flowr.Channel

open Akkling
open Akkling.Streams

open Akka.Actor
open Types

// Channel. Feed of messages for all parties.
type User = string
type ChannelParty = User * IActorRef<ChatProtocolMessage>

// maps user login
type internal ChannelParties = Map<string, ChannelParty>
type internal ChannelState = {
    Parties: ChannelParties
    LastEventId: int
}

let createChannelActor (system: ActorSystem) name =

    let incId chan = { chan with LastEventId = chan.LastEventId + 1}
    let dispatch (parties: ChannelParties) (msg: ChatProtocolMessage): unit =
        parties |> Map.iter (fun _ (_, subscriber) -> subscriber <! msg)
    let allMembers = Map.toSeq >> Seq.map (snd >> fst)

    let behavior state (ctx: Actor<_>): obj -> _ =
        function
        | Terminated (t,_,_) ->
            match state.Parties |> Map.tryFindKey (fun _ (_, ref) -> ref = t) with
            | Some key ->
                {state with Parties = state.Parties |> Map.remove key} |> ignored
            | _ -> state |> ignored

        | :? ChannelCtlMsg as channelEvent ->
            let ts = state.LastEventId, System.DateTime.Now
            match channelEvent with
                | NewParticipant (user, subscriber) ->
                    do monitor ctx subscriber |> ignore
                    let parties = state.Parties |> Map.add user (user, subscriber)
                    do dispatch state.Parties <| ChatProtocolMessage.Joined (ts, user, parties |> allMembers)
                    incId { state with Parties = parties} |> ignored

                | ParticipantLeft user ->
                    let parties = state.Parties |> Map.remove user
                    do dispatch state.Parties <| ChatProtocolMessage.Left (ts, user, parties |> allMembers)
                    incId { state with Parties = parties} |> ignored

                | ReceivedMessage (user, message) ->
                    if state.Parties |> Map.containsKey user then
                        do dispatch state.Parties <| ChatMessage (ts, user, message)
                    incId state |> ignored

                | ListUsers ->
                    let users = state.Parties |> Map.toList |> List.map fst
                    ctx.Sender() <! users
                    state |> ignored
        | _ -> unhandled()

    // TODO * check monitor does work
    in
    props <| actorOf2 (behavior { Parties = Map.empty; LastEventId = 1000 }) |> (spawn system name) |> retype

module FlowExt =
    open Akka.Streams.Dsl
    let to' (sink) (fin: Flow<'TIn,'TOut, 'TFin>) = fin.To(sink)


let createChannel system name =
    let channelActor = createChannelActor system name
    let chatInSink (sender: User) = Sink.toActorRef (ParticipantLeft sender) channelActor

    let partyFlow(sender: User) = // : Akka.Streams.Dsl.Flow<string, ChatProtocolMessage, Akka.NotUsed> =

        let fin =
            Flow.empty<string, Akka.NotUsed>
            |> Flow.map (fun s -> ReceivedMessage(sender, s))
            |> FlowExt.to' (chatInSink sender)

        // The counter-part which is a source that will create a target ActorRef per
        // materialization where the chatActor will send its messages to.
        // This source will only buffer one element and will fail if the client doesn't read
        // messages fast enough.
        let fout =
            Source.actorRef Akka.Streams.OverflowStrategy.Fail 1
            |> Source.mapMaterializedValue (fun (sub: IActorRef<ChatProtocolMessage>) -> channelActor <! NewParticipant (sender, sub); Akka.NotUsed.Instance)

        Flow.ofSinkAndSource fin fout
    in
    channelActor, partyFlow
