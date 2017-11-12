namespace FsChat

[<RequireQualifiedAccess>]
module Protocol =

    type ChanUserInfo = {
        name: string; online: bool; isbot: bool; lastSeen: System.DateTime
    }
    type ChannelInfo = {
        id: string; name: string; userCount: int; topic: string; joined: bool; users: ChanUserInfo list
    }

    type UserEventRec = {
        id: int; ts: System.DateTime; user: ChanUserInfo
    }

    type ChannelMsg = {
        id: int; ts: System.DateTime; text: string; chan: string; author: string    // FIXME id, author and ts are not needed for UserMessage
    }

    type ServerMsg =
        | UserMessage of ChannelMsg

    type HelloInfo = {
        userId: string; nickname: string
        channels: ChannelInfo list
    }

    type ClientErrMsg =
        | AuthFail of string

    /// The messages from server to client
    type ClientMsg =
        | Error of ClientErrMsg
        | Hello of HelloInfo
        | ChanMsg of ChannelMsg
        | UserJoined of UserEventRec * chan: string
        | UserLeft of UserEventRec * chan: string
        | NewChannel of ChannelInfo
        | RemoveChannel of ChannelInfo

