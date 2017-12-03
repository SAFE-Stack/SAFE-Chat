module Diag

open Akka.Actor
open Akkling

open ChannelFlow
open ChatServer

/// Creates an actor for echo bot.
let createEchoActor (system: ActorSystem) botUser =
    let botHandler _ (ctx: Actor<_>) =
        function
        | ChatMessage (_, user, Message message) when not user.isbot ->
            do ctx.Sender() <!| async {
                let reply = sprintf "@%s said: %s" user.nick message
                return NewMessage (botUser, Message reply)
            }
            ()
        | Joined (_, user, _) ->
            do ctx.Sender() <!| async {
                let reply = sprintf "Welcome aboard, %s!" user.nick
                return NewMessage (botUser, Message reply)
            }
        | _ -> ()
        >> ignored
    in
    props <| (actorOf2 <| botHandler ()) |> spawn system "echobot"

let createDiagChannel (system: ActorSystem) (server: IActorRef<_>) (channelName, topic) =
    let echoUser = { Party.Make "echo" with isbot = true }
    let bot = createEchoActor system echoUser

    server <! UpdateState (fun state ->
        state
        |> ServerApi.addChannel (createChannel system) channelName topic
        |> Result.map (
            fun (state, chan) ->
                chan.channelActor <! (NewParticipant (echoUser, bot))
                state
        )
        |> function
        | Ok state -> state
        | Error _ -> state // FIXME log error
    )
