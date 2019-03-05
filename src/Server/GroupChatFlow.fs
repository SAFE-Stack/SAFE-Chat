module GroupChatFlow

open Akkling
open Akkling.Persistence

open Suave.Logging
open ChatTypes

module internal Internals =
    // maps user login
    type ChannelParties = Map<UserId, ClientMessage IActorRef>

    type ChannelState = {
        Parties: ChannelParties
        LastEventId: int
        Messages: MessageInfo list
    }

    let logger = Log.create "chanflow"

    let storeMessage (state: ChannelState) (message: MessageInfo) =
        let messageId = fst message.ts

        { state with
            LastEventId = System.Math.Max(state.LastEventId, messageId)
            Messages = message :: state.Messages }
        

open Internals

let createActorProps<'User, 'Message when 'User: comparison> lastUserLeft =

    let incEventId chan = { chan with LastEventId = chan.LastEventId + 1}
    let dispatch (parties: ChannelParties) (msg: ClientMessage): unit =
        parties |> Map.iter (fun _ subscriber -> subscriber <! msg)
    let allMembers = Map.toSeq >> Seq.map fst

    let handler (ctx: Eventsourced<ChannelMessage>) =

        let rec loop state = actor {
            let! msg = ctx.Receive()
            match msg with
            | ChannelEvent evt ->
                let (MessagePosted msg) = evt
                return loop (storeMessage state msg)

            | ChannelCommand cmd ->
                let ts = state.LastEventId, System.DateTime.Now

                match cmd with
                | NewParticipant (user, subscriber) ->
                    logger.debug (Message.eventX "NewParticipant {user}" >> Message.setFieldValue "user" user)
                    let parties = state.Parties |> Map.add user subscriber
                    do dispatch state.Parties <| Joined (ts, user, parties |> allMembers)
                    return loop <| incEventId { state with Parties = parties}

                | ParticipantLeft user ->
                    logger.debug (Message.eventX "Participant left {user}" >> Message.setFieldValue "user" user)
                    let parties = state.Parties |> Map.remove user
                    do dispatch state.Parties <| Left (ts, user, parties |> allMembers)

                    if parties |> Map.isEmpty then
                        logger.debug (Message.eventX "Last user left the channel")
                        match lastUserLeft with
                        | Some msg -> do ctx.Parent() <! msg
                        | _ -> ()
                    return loop <| incEventId { state with Parties = parties}

                | ParticipantUpdate user ->
                    logger.debug (Message.eventX "Participant updated {user}" >> Message.setFieldValue "user" user)
                    do dispatch state.Parties <| Updated (ts, user)
                    return loop state

                | PostMessage (user, message) ->
                    if state.Parties |> Map.containsKey user then
                        do dispatch state.Parties <| ChatMessage (ts, user, message)
                    return MessagePosted {ts = ts; user = user; message = message} |> ChannelEvent |> Persist

                | ListUsers ->
                    let users = state.Parties |> Map.toList |> List.map fst
                    ctx.Sender() <! users
                    return loop state
        }

        loop { Parties = Map.empty; LastEventId = 1000; Messages = [] }
    in
    propsPersist handler
