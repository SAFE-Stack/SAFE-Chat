namespace FsChat

[<RequireQualifiedAccess>]
module Protocol =

    type ChanUserInfo = {
        nick: string; isbot: bool;
    }
    type ChannelInfo = {
        id: string; name: string; userCount: int; topic: string; joined: bool; users: ChanUserInfo list
    }

    type UserEventRec = {
        id: int; ts: System.DateTime; user: ChanUserInfo
    }

    type UserMessageInfo = {
        text: string; chan: string
    }

    type ServerMsg =
        | Greets
        | UserMessage of UserMessageInfo
        | Join of chanId: string    // TODO add req id (pass back in response message)
        | JoinOrCreate of chanName: string
        | Leave of chanId: string

    type HelloInfo = {
        nick: string
        name: string
        email: string option
        channels: ChannelInfo list
    }

    type ClientErrMsg =
        | AuthFail of string
        | CannotProcess of reqId: string * message: string

    type ChannelMsgInfo = {
        id: int; ts: System.DateTime; text: string; chan: string; author: string
    }

    /// The messages from server to client
    type ClientMsg =
        | Error of ClientErrMsg
        | Hello of HelloInfo
        | ChanMsg of ChannelMsgInfo
        | JoinedChannel of ChannelInfo  // client joined a channel
        | LeftChannel of chanId: string

        // The following types are incomplete
        | UserJoined of UserEventRec * chan: string
        | UserLeft of UserEventRec * chan: string
        | NewChannel of ChannelInfo
        | RemoveChannel of ChannelInfo

