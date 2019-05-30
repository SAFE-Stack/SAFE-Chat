module Chat.State

open Browser.Dom
open Elmish

open Fable.Websockets.Elmish
open Fable.Websockets.Protocol

open Chat.Types
open FsChat

let init () : ChatState * Cmd<Msg> =
    let socketAddr = sprintf "ws://%s/api/socket" document.location.host
    console.debug ("Opening socket", socketAddr)
    NotConnected, Cmd.tryOpenSocket socketAddr

let update msg state : ChatState * Cmd<Msg> = 

    match state, msg with
    | NotConnected, WebsocketMsg (socket, Opened) ->
        Connecting socket, Cmd.ofSocketMessage socket Protocol.ServerMsg.Greets

    | Connecting socket, WebsocketMsg (_, Msg (Protocol.Hello hello)) ->
        let serverData, cmd = RemoteServer.State.init hello
        Connected { socket = socket; serverData = serverData }, cmd |> Cmd.map ApplicationMsg

    | Connected chat, ApplicationMsg amsg ->
        let newServerModel, cmd, serverMsg = RemoteServer.State.update amsg chat.serverData
        let commands =
            [ Cmd.map ApplicationMsg cmd ]
            @ (serverMsg |> Option.map (Cmd.ofSocketMessage chat.socket) |> Option.toList)

        Connected { chat with serverData = newServerModel }, Cmd.batch commands

    | Connected ({ serverData = serverData } as chat), WebsocketMsg (_, Msg msg) ->
        let newServerData, cmd = RemoteServer.State.chatUpdate msg serverData
        Connected { chat with serverData = newServerData }, cmd |> Cmd.map ApplicationMsg

    | _, msg ->
        console.error ("Failed to process message", msg)
        state, Cmd.none
