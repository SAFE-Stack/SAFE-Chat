module ChatData.Types

type UserData = {Nick: string; UserId: string}

type Message = {
    Id: int
    Ts: System.DateTime
    Text: string
    AuthorId: string
}

type ChannelData = {
    Id: string
    Name: string
    Topic: string
    Users: UserData list
    Messages: Message list
}

type ChatData = {
    Me: UserData
    Channels: Map<string, ChannelData>
    Users: Map<string, UserData>
    }

type Chat =
    | NotConnected
    | ChatData of ChatData

type Msg =
    | Connected of UserData * ChannelData list
    | Join of chanName: string  // by name
    | Joined of chan: ChannelData  // by name
    | Leave of chanId: string
    | RecvMessage of chanId: string * Message
    | PostMessage of chanId: string * text: string
    | UserJoined of chanId: string * UserData
    | UserLeft of chanId: string * UserData
    | Disconnected
    | FetchError of exn
