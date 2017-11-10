module Chat.Types

open FsChat
open Fable.Websockets.Client
open Fable.Websockets.Protocol
open Fable.Websockets.Elmish
open Fable.Websockets.Elmish.Types

type UserInfo = {Nick: string; Email: string option; UserId: string}
with static member Anon = {Nick = "anonymous"; Email = None; UserId = "0"}

type Message = {
    Id: int
    Ts: System.DateTime
    Text: string
    AuthorId: string
}

type UsersInfo = | UserCount of int | UserList of UserInfo list

type ChannelData = {
    Id: string
    Name: string
    Topic: string
    Users: UsersInfo
    Messages: Message list
    Joined: bool
} with member this.UserCount = match this.Users with |UserCount c -> c | UserList list -> List.length list

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
    | Hello of UserInfo * ChannelData list
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
    | Disconnected
    | FetchError of exn

// TODO rename to Msg
type MsgType = Msg<Protocol.ServerMsg, Protocol.ClientMsg, AppMsg>
