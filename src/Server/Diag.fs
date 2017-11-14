module Diag

open Akka.Actor
open Akkling

open ChannelFlow
open ChatServer

/// Creates an actor for echo bot.
let createEchoActor (system: ActorSystem) botNick =
    let isBot (userNick: UserNick) = userNick = botNick // FIXME

    let botHandler _ (ctx: Actor<_>) =
        function
        | ChatMessage (_, (userNick: UserNick), Message message) when not (isBot userNick) ->
            do ctx.Sender() <!| async {
                let (UserNick nickname) = userNick
                let reply = sprintf "@%s said: %s" nickname message
                return NewMessage (botNick, Message reply)
            }
            ()
        | Joined (_, UserNick nickname, _) ->
            do ctx.Sender() <!| async {
                let reply = sprintf "Welcome aboard, \"@%s\"!" nickname
                return NewMessage (botNick, Message reply)
            }
        | _ -> ()
        >> ignored
    in
    props <| (actorOf2 <| botHandler ()) |> spawn system "echobot"

let createDiagChannel (system: ActorSystem) (server: IActorRef<_>) (channelName, topic) =
    let echoNick = UserNick "echo"
    let bot = createEchoActor system echoNick

    server <! UpdateState (fun state ->
        state
        |> ServerApi.addChannel (createChannel system) channelName topic
        |> Result.map (
            fun (state, chan) ->
                chan.channelActor <! (NewParticipant (echoNick, bot))
                state
        )
        |> function
        | Ok state -> state
        | Result.Error e -> state // FIXME log error
    )

