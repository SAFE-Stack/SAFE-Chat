module Channel.Types

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
    PostText: string
} with
    member this.UserCount = match this.Users with |UserCount c -> c | UserList list -> List.length list
    static member Empty = {Id = null; Name = null; Topic = ""; Users = UserCount 0; Messages = []; Joined = false; PostText = ""}

type Msg =
    | SetPostText of string
    | PostText
    | Forward of FsChat.Protocol.ChannelMsg
    | Leave