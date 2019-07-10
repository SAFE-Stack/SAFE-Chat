module ChatServer.Types

open Channel.Types;

type Model = {
    Me: Channel.Types.UserInfo
    ChannelList: Map<ChannelId, ChannelInfo>
    Channels: Map<ChannelId, Channel.Types.Model>
    NewChanName: string option   // name for new channel (part of SetCreateChanName), None - panel is hidden
}

type Msg =
    | Nop
    | ChannelMsg of ChannelId * Channel.Types.Msg
    | SetNewChanName of string option
    | CreateJoin
    | Join of chanId: string

    | Leave of chanId: string
    | ServerMessage of FsChat.Protocol.ClientMsg
