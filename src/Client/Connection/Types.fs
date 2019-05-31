module Connection.Types

open FsChat
open Fable.Websockets.Elmish

type ConnectionInfo = {
    socket: SocketHandle<Protocol.ServerMsg>
    serverData: ChatServer.Types.Model
}

type Model =
    | NotConnected
    | Connecting of SocketHandle<Protocol.ServerMsg>
    | Connected of ConnectionInfo

type Msg = Msg<Protocol.ServerMsg, Protocol.ClientMsg, ChatServer.Types.Msg>
