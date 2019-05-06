module ChatTypes

type UserId = UserId of string
type Message = Message of string
type ChannelId = ChannelId of int

// message timestamp
type Timestamp = int * System.DateTime

type ChatMsgInfo = { ts: Timestamp; author: UserId; message: Message }
type UserUpdatedMsgInfo = { ts: Timestamp; user: UserId }
type ChangedPartiesMsgInfo = { ts: Timestamp; user: UserId; all: UserId seq }

type ChannelInfo = {
    ts: Timestamp;
    users: UserId seq
    messageCount: int
    unreadMessageCount: int option
    lastMessages: ChatMsgInfo list }

/// Client protocol message (messages sent from channel to client actor)
type ClientMessage =
    | ChatMessage of ChatMsgInfo
    | Joined of ChangedPartiesMsgInfo
    | Left of ChangedPartiesMsgInfo
    | UserUpdated of UserUpdatedMsgInfo
    /// this message is sent to a client upon connection
    | JoinedChannel of ChannelInfo

/// Channel actor protocol (server side protocol)
type ChannelCommand =
    | NewParticipant of user: UserId * subscriber: ClientMessage Akkling.ActorRefs.IActorRef
    | ParticipantLeft of UserId
    | ParticipantUpdate of UserId
    | PostMessage of UserId * Message
    | ListUsers

/// Channel actor protocol (server side protocol)
type ChannelEvent =
    | MessagePosted of ChatMsgInfo

type ChannelMessage =
    | ChannelCommand of ChannelCommand
    | ChannelEvent of ChannelEvent