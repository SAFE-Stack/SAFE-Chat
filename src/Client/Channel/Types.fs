module Channel.Types

type UserInfo = {
    Id: string
    Nick: string; Status: string
    IsBot: bool; Online: bool; ImageUrl: string option; isMe: bool}
with static member Anon = {Id = "0"; Nick = "anonymous"; Status = ""; IsBot = false; Online = true; ImageUrl = None; isMe = false}

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
    | Forward of string
    | Leave