module Channel.Types

type UserInfo = {Nick: string; IsBot: bool; Online: bool}
with static member Anon = {Nick = "anonymous"; IsBot = false; Online = true}

type MessageContent =
    | UserMessage of text: string * author: UserInfo
    | SystemMessage of text: string

type Message = {
    Id: int
    Ts: System.DateTime
    Content: MessageContent
}

type UsersInfo = | UserCount of int | UserList of Map<string, UserInfo>

type ChannelData = {
    Id: string
    Name: string
    Topic: string
    Users: UsersInfo
    Messages: Message list
    Joined: bool
    PostText: string
} with
    member this.UserCount = match this.Users with |UserCount c -> c | UserList list -> Map.count list
    static member Empty = {Id = null; Name = null; Topic = ""; Users = UserCount 0; Messages = []; Joined = false; PostText = ""}

type Msg =
    | SetPostText of string
    | PostText
    | Forward of FsChat.Protocol.UserMessageInfo
    | Leave