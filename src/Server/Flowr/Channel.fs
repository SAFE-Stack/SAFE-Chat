module fschat.Flowr.Channel

open Akka.Actor
open Akkling
open Akkling.Streams

open Types

// Channel. Feed of messages for all parties.
type ChannelParty = User * IActorRef<ChatProtocolMessage>

// maps user login
type internal ChannelParties = Map<string, ChannelParty>
type internal ChannelState = {
    Parties: ChannelParties
    LastEventId: int
}

/// Creates channel actor
let createChannel (system: ActorSystem) name =

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
            | _ -> ignored state

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
    props <| actorOf2 (behavior { Parties = Map.empty; LastEventId = 1000 }) |> (spawn system name) |> retype

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
        |> Source.mapMaterializedValue (fun (sub: IActorRef<ChatProtocolMessage>) -> channelActor <! NewParticipant (user, sub); Akka.NotUsed.Instance)

    Flow.ofSinkAndSource fin fout
