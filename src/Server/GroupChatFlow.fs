module GroupChatFlow

open Akka.Actor
open Akkling

open Suave.Logging

// message timestamp
type Timestamp = int * System.DateTime

type ChannelConfig = {
    // instructs channel actor to shutdown channel after last member exited
    autoRemove: bool
} with static member Default = {autoRemove = false}

/// Client protocol message (messages sent from channel to client actor)
type ClientMessage<'User, 'Message> =
    | ChatMessage of ts: Timestamp * author: 'User * 'Message
    | Joined of ts: Timestamp * user: 'User * all: 'User seq
    | Left of ts: Timestamp * user: 'User * all: 'User seq
    | Updated of ts: Timestamp * user: 'User

/// Channel actor protocol (server side protocol)
type ChannelMessage<'User, 'Message> =
    | NewParticipant of user: 'User * subscriber: ClientMessage<'User, 'Message> IActorRef
    | ParticipantLeft of 'User
    | ParticipantUpdate of 'User
    | NewMessage of 'User * 'Message
    | ListUsers

module internal Internals =
    // maps user login
    type ChannelParties<'User, 'Message> when 'User: comparison = Map<'User, ClientMessage<'User, 'Message> IActorRef>

    type ChannelState<'User, 'Message> when 'User: comparison = {
        Config: ChannelConfig
        Parties: ChannelParties<'User, 'Message>
        LastEventId: int
    }

    let logger = Log.create "chanflow"

open Internals

let createChannelActor<'User, 'Message when 'User: comparison> (system: IActorRefFactory) (config: ChannelConfig) =

    let incId chan = { chan with LastEventId = chan.LastEventId + 1}
    let dispatch (parties: ChannelParties<'User, 'Message>) (msg: ClientMessage<'User, 'Message>): unit =
        parties |> Map.iter (fun _ subscriber -> subscriber <! msg)
    let allMembers = Map.toSeq >> Seq.map fst

    let rec behavior state (ctx: Actor<_>) =
        let updateState newState = become (behavior newState ctx) in
        let ts = state.LastEventId, System.DateTime.Now
        function
        | NewParticipant (user, subscriber) ->
            logger.debug (Message.eventX "NewParticipant {user}" >> Message.setFieldValue "user" user)
            let parties = state.Parties |> Map.add user subscriber
            do dispatch state.Parties <| Joined (ts, user, parties |> allMembers)
            incId { state with Parties = parties} |> updateState

        | ParticipantLeft user ->
            logger.debug (Message.eventX "Participant left {user}" >> Message.setFieldValue "user" user)
            let parties = state.Parties |> Map.remove user
            do dispatch state.Parties <| Left (ts, user, parties |> allMembers)

            if config.autoRemove && parties |> Map.isEmpty then
                logger.debug (Message.eventX "It was the last participant, closing the channel")
                stop()
            else
                incId { state with Parties = parties} |> updateState

        | ParticipantUpdate user ->
            logger.debug (Message.eventX "Participant updated {user}" >> Message.setFieldValue "user" user)
            do dispatch state.Parties <| Updated (ts, user)
            ignored state

        | NewMessage (user, message) ->
            if state.Parties |> Map.containsKey user then
                do dispatch state.Parties <| ChatMessage (ts, user, message)
            incId state |> updateState

        | ListUsers ->
            let users = state.Parties |> Map.toList |> List.map fst
            ctx.Sender() <! users
            ignored state

    in
    props <| actorOf2 (behavior { Parties = Map.empty; LastEventId = 1000; Config = config }) |> (spawn system null)
