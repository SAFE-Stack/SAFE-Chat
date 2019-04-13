namespace FsChat

[<RequireQualifiedAccess>]
module Protocol =

    type UserId = string
    type ChannelId = string

    type ChannelMsgInfo = {
        id: int; ts: System.DateTime; text: string; chan: ChannelId; author: UserId
    }

    type ChanUserInfo = {
        id: UserId; nick: string; isbot: bool; status: string; email: string; imageUrl: string
    }
    type ChannelInfo = {
        id: ChannelId; name: string; userCount: int; topic: string; joined: bool; users: ChanUserInfo list  // TODO remove user list and joined
    }

    type ActiveChannelInfo = {  // FIXME name
        info: ChannelInfo
        // users: ChanUserInfo list
        messageCount: int
        unreadMessageCount: int option
        lastMessages: ChannelMsgInfo list
    }

    type UserMessageInfo = {text: string; chan: ChannelId}
    type UserCommandInfo = {command: string; chan: ChannelId}

    type ServerCommand =
        | UserCommand of UserCommandInfo
        | Join of ChannelId
        | JoinOrCreate of channelName: string
        | Leave of ChannelId
        | Ping

    type ServerMsg =
        | Greets
        | UserMessage of UserMessageInfo
        | ServerCommand of reqId: string * message: ServerCommand

    type HelloInfo = {
        me: ChanUserInfo
        channels: ChannelInfo list
        // TODO separate all channels from active channels
    }

    type ClientErrMsg =
        | AuthFail of string
        | CannotProcess of string

    type ChannelEventKind =
        | Joined of ChannelId * ChanUserInfo
        | Left of ChannelId * UserId
        | Updated of ChannelId * ChanUserInfo

    type ChannelEventInfo = {
        id: int; ts: System.DateTime
        evt: ChannelEventKind
    }

    type CommandResponse =
        | Error of ClientErrMsg
        | UserUpdated of ChanUserInfo
        | JoinedChannel of ActiveChannelInfo  // client joined a channel
        | LeftChannel of chanId: string
        | Pong

    /// The messages from server to client
    type ClientMsg =
        | Hello of HelloInfo
        | CmdResponse of reqId: string * reply: CommandResponse

        // external events
        | ChanMsg of ChannelMsgInfo
        | ChannelEvent of ChannelEventInfo
        | NewChannel of ChannelInfo
        | RemoveChannel of ChannelInfo
        | ChannelInfo of ActiveChannelInfo

