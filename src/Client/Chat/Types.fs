module Chat.Types

open FsChat
open Fable.Websockets.Elmish
open Fable.Websockets.Elmish.Types

open Channel.Types

type ChatData = {
    socket: SocketHandle<Protocol.ServerMsg, Protocol.ClientMsg>
    Channels: Map<string, ChannelData>
    NewChanName: string option   // name for new channel (part of SetCreateChanName), None - panel is hidden
    }
    with static member Empty = {socket = SocketHandle.Blackhole(); Channels = Map.empty; NewChanName = None}

type ChatState =
    | NotConnected
    | Connected of UserInfo * ChatData

// TODO shorten the list
type AppMsg =
    | Nop
    | ChannelMsg of chanId: string * Channel.Types.Msg
    | SetNewChanName of string option
    | CreateJoin
    | Join of chanId: string

    //| Joined of chan: ChannelData  // by name
    | Leave of chanId: string
    | Left of chanId: string

// TODO rename to Msg
type MsgType = Msg<Protocol.ServerMsg, Protocol.ClientMsg, AppMsg>
