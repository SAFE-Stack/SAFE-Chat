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

    type ServerMsg = {
        id: int; ts: System.DateTime; text: string; chan: string; author: string
    }

    type Hello = {
        userId: string; nickname: string
        channels: ChannelInfo list
    }

    /// The messages from server to client
    type ClientMsg =
        | Hello of Hello
        | ChanMsg of ServerMsg
        | UserJoined of UserEventRec * chan: string
        | UserLeft of UserEventRec * chan: string
        | NewChannel of ChannelInfo
        | RemoveChannel of ChannelInfo

