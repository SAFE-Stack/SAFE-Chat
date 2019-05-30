module Chat.Types

open FsChat
open Fable.Websockets.Elmish

type ConnectionInfo = {
    socket: SocketHandle<Protocol.ServerMsg>
    serverData: RemoteServer.Types.Model
}

type ChatState =
    | NotConnected
    | Connecting of SocketHandle<Protocol.ServerMsg>
    | Connected of ConnectionInfo

type Msg = Msg<Protocol.ServerMsg, Protocol.ClientMsg, RemoteServer.Types.Msg>
