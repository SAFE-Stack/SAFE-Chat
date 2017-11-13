module Channel.Types

type UserInfo = {UserId: string; Nick: string; IsBot: bool; Online: bool}
with static member Anon = {Nick = "anonymous"; UserId = "0"; IsBot = false; Online = false}

type Message = {
    Id: int
    Ts: System.DateTime
    Text: string
    AuthorId: string
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
    | Forward of FsChat.Protocol.ChannelMsg
    | Leave