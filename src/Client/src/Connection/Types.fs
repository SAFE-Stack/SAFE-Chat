module Connection.Types

open Elmish
open FsChat
open Websockets.Elmish

type ConnectionInfo = {
    // socket: SocketHandle<Protocol.ServerMsg>
    serverData: ChatServer.Types.Model
    socket: SocketHandle<Protocol.ServerMsg>
}

type Model =
    | NotConnected
    | Initializing of SocketHandle<Protocol.ServerMsg>
    | Connected of ConnectionInfo

type Msg =
    | WebsocketMsg of WebsocketEvent<Protocol.ServerMsg, Protocol.ClientMsg>    // Message to be forwarded to websocket
    | ServerMsg of Protocol.ServerMsg // Message from server
    | ApplicationMsg of ChatServer.Types.Msg
    | NoOp // Placeholder for unhandled messages