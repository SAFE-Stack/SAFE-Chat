module Diag

open Akka.Actor
open Akkling

open ChannelFlow
open ChatServer

/// Creates an actor for echo bot.
let createEchoActor (system: ActorSystem) botUserId =
    let getUserName uid = async {return uid.ToString()}    // FIXME display user nickname

    let botHandler state (ctx: Actor<_>) =
        function
        | ChatMessage (_, (userId: Uuid), Message message) ->
            do ctx.Sender() <!| async {
                let! userName = getUserName userId
                let reply = sprintf "\"%s\" said: %s" userName message
                return ChannelMessage.NewMessage (botUserId, Message reply)
            }
            ()
             // FIXME do not let bots reply to other bots when user.Person <> Person.Anonymous
        | Joined (_, userId, _) ->
            do ctx.Sender() <!| async {
                let! userName = getUserName userId
                let reply = sprintf "Welcome aboard, \"%s\"!" userName
                return ChannelMessage.NewMessage (botUserId, Message reply)
            }
        | _ -> ()
        >> ignored
    in
    props <| (actorOf2 <| botHandler ()) |> spawn system "echobot"

let createDiagChannel (system: ActorSystem) (server: IActorRef<_>) channelName =
    let botUserId = {Uuid.i1 = 10000L; i2 = 1}
    let bot = createEchoActor system botUserId

    server <! UpdateState (fun state ->
        state
        |> ServerApi.addChannel (createChannel system) channelName
        |> Result.map (
            fun (state, chan) ->
                chan.channelActor <! (NewParticipant (botUserId, bot))
                state
        )
        |> function
        | Ok state -> state
        | Result.Error e -> state // FIXME log error
    )

