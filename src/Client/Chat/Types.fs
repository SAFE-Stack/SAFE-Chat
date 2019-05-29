module Chat.Types

open FsChat
open Fable.Websockets.Elmish

// TODO rename to a ServerInfo
type ChatInfo = {
    socket: SocketHandle<Protocol.ServerMsg>
    user: Channel.Types.UserInfo
    serverData: RemoteServer.Types.Model
}

type ChatState =
    | NotConnected
    | Connected of ChatInfo

type Msg = Msg<Protocol.ServerMsg, Protocol.ClientMsg, RemoteServer.Types.Msg>
