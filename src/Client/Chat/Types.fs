module Chat.Types

open FsChat
open Fable.Websockets.Elmish
open Fable.Websockets.Elmish.Types

open Channel.Types

type ChatData = {
    socket: SocketHandle<Protocol.ServerMsg, Protocol.ClientMsg>
    Channels: Map<string, ChannelData>
    Users: Map<string, UserInfo>
    NewChanName: string     // name for new channel (part of SetCreateChanName)
    }
    with static member Empty = {socket = SocketHandle.Blackhole(); Channels = Map.empty; Users = Map.empty; NewChanName = ""}

type ChatState =
    | NotConnected
    | Connected of UserInfo * ChatData

// TODO shorten the list
type AppMsg =
    | Nop
    | ChannelMsg of chanId: string * Channel.Types.Msg
    | SetNewChanName of string
    | CreateJoin
    | Join of chanId: string
    | Joined of chan: ChannelData  // by name
    | Leave of chanId: string
    | Left of chanId: string
    | RecvMessage of chanId: string * Message
    | PostMessage of chanId: string * text: string
    | UserJoined of chanId: string * UserInfo
    | UserLeft of chanId: string * UserInfo
    | FetchError of exn

// TODO rename to Msg
type MsgType = Msg<Protocol.ServerMsg, Protocol.ClientMsg, AppMsg>
