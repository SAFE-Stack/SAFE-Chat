module Diag

open Akka.Actor
open Akkling

open ChatUser
open ChannelFlow
open ChatServer

/// Creates an actor for echo bot.
let createEchoActor (getUser: GetUser) (system: ActorSystem) botUser =

    let forUser userid fn = async {
        let! user = getUser userid
        return user |> Option.bind(function |User { nick = nickName } -> Some (fn nickName) | _ -> None)
    }

    let handler (ctx: Actor<_>) =
        let rec loop () = actor {
            let! msg = ctx.Receive()
            let! reply =
                match msg with
                | ChatMessage (_, userid, Message message) ->
                    forUser userid (fun nickName -> sprintf "%s said: %s" nickName message)
                | Joined (_, userid, _) ->
                    forUser userid (fun nickName -> sprintf "Welcome aboard, %s!" nickName)
                | _ -> async.Return None

            match reply with
            | Some reply -> do ctx.Sender() <! NewMessage (botUser, Message reply)
            | _ -> ()

            return! loop()
        }
        loop()
    in
    spawn system "echobot" <| props(handler)

let createDiagChannel (getUser: GetUser) (system: ActorSystem) (server: IActorRef<_>) (channelName, topic) =
    let echoUser = ChatUser.makeBot "echo"
    let bot = createEchoActor getUser system echoUser

    server <! UpdateState (fun state ->
        state
        |> ServerApi.addChannel (fun () -> createChannel system) channelName topic
        |> Result.map (
            fun (state, chan) ->
                chan.channelActor <! (NewParticipant (ChatUser.getUserId echoUser, bot))
                state
        )
        |> function
        | Ok state -> state
        | Error _ -> state // FIXME log error
    )
