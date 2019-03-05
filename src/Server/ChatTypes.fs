module ChatTypes

open Akkling

type UserId = UserId of string
type Message = Message of string
type ChannelId = ChannelId of int

// message timestamp
type Timestamp = int * System.DateTime

/// Client protocol message (messages sent from channel to client actor)
type ClientMessage =
    | ChatMessage of ts: Timestamp * author: UserId * Message
    | Joined of ts: Timestamp * user: UserId * all: UserId seq
    | Left of ts: Timestamp * user: UserId * all: UserId seq
    | Updated of ts: Timestamp * user: UserId

/// Channel actor protocol (server side protocol)
type ChannelCommand =
    | NewParticipant of user: UserId * subscriber: ClientMessage IActorRef
    | ParticipantLeft of UserId
    | ParticipantUpdate of UserId
    | PostMessage of UserId * Message
    | ListUsers

/// Channel actor protocol (server side protocol)
type MessageInfo = { ts: Timestamp; user: UserId; message: Message }
type ChannelEvent =
    | MessagePosted of MessageInfo

type ChannelMessage =
    | ChannelCommand of ChannelCommand
    | ChannelEvent of ChannelEvent