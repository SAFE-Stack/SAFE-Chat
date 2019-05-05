module AboutChannelActor

open Akkling
open Suave.Logging

open ChatTypes

let private logger = Log.create "aboutflow"

let private aboutMessage =
    [   """## Welcome to F# Chat

F# Chat application built with Fable, Elmish, React, Suave, Akka.Streams, Akkling"""

        "Click on the channel name to join or click '+' and type in the name of the new channel."

        """Try the following commands in channel's input box:

* **/leave** - leaves the channel
* **/join <chan name>** - joins the channel, creates if it doesn't exist
* **/nick <newnick>** - changes your nickname
* **/status <newstatus>** - change status
* **/avatar <imageUrl>** - change user avatar
""" ]

let props systemUser =

    // TODO put to actor state, otherwise only one instance would be supported
    let mutable users = Map.empty
    let mkChatMessage message =
        ChatMessage { ts = (0, System.DateTime.Now); author =systemUser; message = Message message }

    let rec behavior (ctx: Actor<_>) =
        function
        | ChannelCommand (NewParticipant (user, subscriber)) ->
            users <- users |> Map.add user subscriber
            logger.debug (Message.eventX "Sending about to {user}" >> Message.setFieldValue "user" user)

            aboutMessage |> List.indexed |> List.iter (fun (i, msgText) ->
                ctx.System.Scheduler.ScheduleTellOnce( System.TimeSpan.FromMilliseconds(400. * float i), subscriber, mkChatMessage msgText)
                )
            // sending messages with some delay. Sending while flow is initialized causes intermittently dropped messages
            ignored ()

        | ChannelCommand (ParticipantLeft user) ->
            users <- users |> Map.remove user
            logger.debug (Message.eventX "Participant left {user}" >> Message.setFieldValue "user" user)
            ignored ()

        | ChannelCommand (PostMessage (user, _)) ->
            let sub = users |> Map.find user
            do sub <! mkChatMessage "> Sorry, this feature is not implemented yet."
            ignored ()

        | ChannelCommand ListUsers ->
            do ctx.Sender() <! [systemUser]
            ignored ()

        | _ ->
            ignored ()

    in
    props <| actorOf2 behavior

