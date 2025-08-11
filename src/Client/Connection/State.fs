module Connection.State

open Browser.Dom
open Elmish
open Thoth.Json

open Websockets.Elmish
open FsChat
open Types

let serializeServerMsg (msg: Protocol.ServerMsg) : string =
    Encode.Auto.toString<Protocol.ServerMsg>(0, msg)

let deserializeClientMsg: string -> Protocol.ClientMsg option =
    Decode.Auto.fromString<Protocol.ClientMsg> >>
    function
    | Ok msg -> Some msg
    | _ -> None

module Cmd =
    let public ofSocketMessage (socket: SocketHandle<'serverMsg>) (message:'serverMsg) : Elmish.Cmd<Msg> =
        [fun _ -> socket.send message]

    let public connectSocket (socketAddr: string) : Elmish.Cmd<Msg> =
        Cmd.ofEffect (fun dispatch ->
            connectWebSocket socketAddr serializeServerMsg deserializeClientMsg (WebsocketMsg >> dispatch) |> ignore
        )

let init () : Model * Cmd<Msg> =
    let socketAddr = sprintf "ws://%s/api/socket" document.location.host
    console.debug ("Opening socket", socketAddr)
    NotConnected, Cmd.connectSocket socketAddr
    
let rec update msg state : Model * Cmd<Msg> = 

    match state, msg with
    | NotConnected, WebsocketMsg (Opened socket) ->
        Initializing socket, Cmd.ofSocketMessage socket Protocol.ServerMsg.Greets

    | Initializing socket, WebsocketMsg (Message (Protocol.Hello hello)) ->
        let serverData, cmd = ChatServer.State.init hello
        let connectionInfo = { serverData = serverData; socket = socket }
        Connected connectionInfo, cmd |> Cmd.map ApplicationMsg

    | Connected _, WebsocketMsg (Message msg) ->
        update (ApplicationMsg <| ChatServer.Types.ServerMessage msg) state

    | Connected chat, ApplicationMsg amsg ->
        let newServerModel, cmd, serverMsg = ChatServer.State.update amsg chat.serverData
        let effect = serverMsg |> Option.map (fun msg -> Cmd.ofEffect (fun _ -> chat.socket.send msg))

        let commands = [ Cmd.map ApplicationMsg cmd ] @ (effect |> Option.toList)

        Connected { chat with serverData = newServerModel }, Cmd.batch commands

    // TODO close and error handling

    | _, msg ->
        console.error ("Failed to process message", msg)
        state, Cmd.none