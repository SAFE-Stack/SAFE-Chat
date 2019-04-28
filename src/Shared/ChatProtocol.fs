namespace FsChat

[<RequireQualifiedAccess>]
module Protocol =

    type UserId = string
    type ChannelId = string
    type RequestId = string

    type ChannelMessageInfo = {
        id: int; ts: System.DateTime; text: string; chan: ChannelId; author: UserId
    }
    type ChanUserInfo = {
        id: UserId; nick: string; isbot: bool; status: string; email: string; imageUrl: string
    }
    type ChannelInfo = {
        id: ChannelId; name: string; userCount: int; topic: string
    }

    type ActiveChannelData = {
        channelId: ChannelId
        users: ChanUserInfo list
        messageCount: int
        unreadMessageCount: int option
        lastMessages: ChannelMessageInfo list
    }

    type UserMessageData = {text: string; chan: ChannelId}
    type UserCommandData = {command: string; chan: ChannelId}

    type ServerCommand =
        | UserCommand of UserCommandData
        | Join of ChannelId
        | JoinOrCreate of channelName: string
        | Leave of ChannelId
        | Ping

    type ServerMsg =
        | Greets
        | UserMessage of UserMessageData
        | ServerCommand of reqId: RequestId * message: ServerCommand

    type HelloInfo = {
        me: ChanUserInfo
        channels: ChannelInfo list
    }

    type ClientErrMsg =
        | AuthFail of string
        | CannotProcess of string

    type ChannelEvent =
        | Joined of ChanUserInfo
        | Left of UserId
        | Updated of ChanUserInfo

    type ServerEvent =
        | NewChannel of ChannelInfo
        | RemoveChannel of ChannelInfo
        | ChannelEvent of ChannelId * ChannelEvent
        | JoinedChannel of ActiveChannelData

    type ServerEventInfo = {
        id: int; ts: System.DateTime
        evt: ServerEvent
    }

    type CommandResponse =
        | Error of ClientErrMsg
        | UserUpdated of ChanUserInfo
        | JoinedChannel of ChannelInfo  // client joined a channel
        | LeftChannel of chanId: string
        | Pong

    /// The messages from server to client
    type ClientMsg =
        | Hello of HelloInfo
        | CmdResponse of reqId: RequestId * reply: CommandResponse

        // external events
        | ChanMsg of ChannelMessageInfo
        | ServerEvent of ServerEventInfo
