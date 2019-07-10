module Connection.State

open Browser.Dom
open Elmish

open Fable.Websockets.Elmish
open Fable.Websockets.Protocol

open Types
open FsChat

let init () : Model * Cmd<Msg> =
    let socketAddr = sprintf "ws://%s/api/socket" document.location.host
    console.debug ("Opening socket", socketAddr)
    NotConnected, Cmd.tryOpenSocket socketAddr

let rec update msg state : Model * Cmd<Msg> = 

    match state, msg with
    | NotConnected, WebsocketMsg (socket, Opened) ->
        Connecting, Cmd.ofSocketMessage socket Protocol.ServerMsg.Greets

    | Connecting, WebsocketMsg (socket, Msg (Protocol.Hello hello)) ->
        let serverData, cmd = ChatServer.State.init hello
        Connected { socket = socket; serverData = serverData }, cmd |> Cmd.map ApplicationMsg

    | Connected chat, ApplicationMsg amsg ->
        let newServerModel, cmd, serverMsg = ChatServer.State.update amsg chat.serverData
        let commands =
            [ Cmd.map ApplicationMsg cmd ]
            @ (serverMsg |> Option.map (Cmd.ofSocketMessage chat.socket) |> Option.toList)

        Connected { chat with serverData = newServerModel }, Cmd.batch commands

    | Connected _, WebsocketMsg (_, Msg msg) ->
        update (ApplicationMsg <| ChatServer.Types.ServerMessage msg) state

    | _, msg ->
        console.error ("Failed to process message", msg)
        state, Cmd.none
