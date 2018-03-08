namespace FsChat

[<RequireQualifiedAccess>]
module Protocol =

    type UserId = string
    type ChannelId = string

    type ChanUserInfo = {
        id: UserId; nick: string; isbot: bool; status: string; email: string; imageUrl: string
    }
    type ChannelInfo = {
        id: ChannelId; name: string; userCount: int; topic: string; joined: bool; users: ChanUserInfo list
    }

    type UserMessageInfo = {
        text: string; chan: ChannelId
    }

    type ServerMsg =
        | Greets
        | UserMessage of UserMessageInfo
        | ControlCommand of UserMessageInfo
        | Join of ChannelId    // TODO add req id (pass back in response message)
        | JoinOrCreate of channelName: string
        | Leave of ChannelId

    type HelloInfo = {
        me: ChanUserInfo
        channels: ChannelInfo list
    }

    type ClientErrMsg =
        | AuthFail of string
        | CannotProcess of reqId: string * message: string

    type ChannelMsgInfo = {
        id: int; ts: System.DateTime; text: string; chan: ChannelId; author: UserId
    }

    type ChannelEventKind =
        | Joined of ChannelId * ChanUserInfo
        | Left of ChannelId * UserId
        | Updated of ChannelId * ChanUserInfo

    type ChannelEventInfo = {
        id: int; ts: System.DateTime
        evt: ChannelEventKind
    }

    /// The messages from server to client
    type ClientMsg =
        | Error of ClientErrMsg
        | Hello of HelloInfo
        | UserUpdated of ChanUserInfo
        | ChanMsg of ChannelMsgInfo
        | JoinedChannel of ChannelInfo  // client joined a channel
        | LeftChannel of chanId: string

        // The following types are incomplete
        | ChannelEvent of ChannelEventInfo
        | NewChannel of ChannelInfo
        | RemoveChannel of ChannelInfo

