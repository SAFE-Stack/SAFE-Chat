module ChatData.Types

type UserData = {Nick: string; UserId: string}

type Message = {
    Id: int
    Ts: System.DateTime
    Text: string
    AuthorId: string
}

type UsersInfo = | UserCount of int | UserList of UserData list

type ChannelData = {
    Id: string
    Name: string
    Topic: string
    Users: UsersInfo
    Messages: Message list
    Joined: bool
} with member this.UserCount = match this.Users with |UserCount c -> c | UserList list -> List.length list

type ChatData = {
    Me: UserData
    Channels: Map<string, ChannelData>
    Users: Map<string, UserData>
    }

type Chat =
    | NotConnected
    | ChatData of ChatData

type Msg =
    | Nop
    | Connected of UserData * ChannelData list
    | Join of chanId: string
    | Joined of chan: ChannelData  // by name
    | Leave of chanId: string
    | Left of chanId: string
    | RecvMessage of chanId: string * Message
    | PostMessage of chanId: string * text: string
    | UserJoined of chanId: string * UserData
    | UserLeft of chanId: string * UserData
    | Disconnected
    | FetchError of exn
