module GroupChatChannelActor

open System
open Akkling
open Akkling.Persistence

open Suave.Logging
open ChatTypes

module private Internals =
    // maps user login
    type ChannelParties = Map<UserId, ClientMessage IActorRef>

    type ChannelState = {
        Parties: ChannelParties
        LastEventId: int
        Messages: ChatMsgInfo list }

    let logger = Log.create "chanflow"

    let storeMessage (state: ChannelState) (message: ChatMsgInfo) =
        let messageId = fst message.ts

        { state with
            LastEventId = System.Math.Max(state.LastEventId, messageId)
            Messages = message :: state.Messages }

open Internals

let props<'User, 'Message when 'User: comparison> lastUserLeft =

    let dispatch (parties: ChannelParties) (msg: ClientMessage): unit =
        parties |> Map.iter (fun _ sub -> sub <! msg)
    let allMembers = Map.toSeq >> Seq.map fst

    let mkChannelInfo state =
        {   ts = state.LastEventId, System.DateTime.Now
            users = state.Parties |> allMembers
            messageCount = state.Messages |> List.length
            unreadMessageCount = None
            lastMessages = state.Messages |> List.truncate 10 }  // FIXME const

    let handler (ctx: Eventsourced<ChannelMessage>) =

        let rec loop state = actor {
            let! msg = ctx.Receive()
            match msg with
            | ChannelEvent evt ->
                let (MessagePosted msg) = evt
                return loop (storeMessage state msg)

            | ChannelCommand cmd ->
                let eventId = state.LastEventId + 1
                let ts = eventId, System.DateTime.Now
                let mkPartiesMsgInfo (f, user, parties) = f { ts = ts; user = user;all = parties |> allMembers }

                match cmd with
                | NewParticipant (user, subscriber) ->
                    logger.debug (Message.eventX "NewParticipant {user}" >> Message.setFieldValue "user" user)

                    let parties = state.Parties |> Map.add user subscriber
                    let newState = { state with LastEventId = eventId; Parties = parties }

                    subscriber <! JoinedChannel (mkChannelInfo newState)
                    do dispatch state.Parties <| mkPartiesMsgInfo (Joined, user, parties)

                    // ctx.Schedule (TimeSpan.FromMilliseconds 5.) subscriber (JoinedChannel (mkChannelInfo newState)) |> ignore

                    return loop newState

                | ParticipantLeft user ->
                    logger.debug (Message.eventX "Participant left {user}" >> Message.setFieldValue "user" user)
                    let parties = state.Parties |> Map.remove user
                    do dispatch state.Parties <| mkPartiesMsgInfo (Left, user, parties)

                    if parties |> Map.isEmpty then
                        logger.debug (Message.eventX "Last user left the channel")
                        match lastUserLeft with
                        | Some msg -> do ctx.Parent() <! msg
                        | _ -> ()
                    return loop <| { state with LastEventId = eventId; Parties = parties }

                | ParticipantUpdate user ->
                    logger.debug (Message.eventX "Participant updated {user}" >> Message.setFieldValue "user" user)
                    do dispatch state.Parties <| UserUpdated { ts = ts; user = user }
                    return loop { state with LastEventId = eventId }

                | PostMessage (user, message) ->
                    let messageInfo = {ts = ts; author = user; message = message}
                    if state.Parties |> Map.containsKey user then
                        do dispatch state.Parties <| ChatMessage messageInfo
                    return MessagePosted messageInfo |> ChannelEvent |> Persist

                | ListUsers ->
                    let users = state.Parties |> (Map.toList >> List.map fst)
                    ctx.Sender() <! users
                    return loop state
        }

        loop { Parties = Map.empty; LastEventId = 1000; Messages = [] }
    in
    propsPersist handler