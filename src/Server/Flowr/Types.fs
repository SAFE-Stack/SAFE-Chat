module fschat.Flowr.Types

open System
open Akkling

type Uuid = string
type User = string
type Message = Message of string
type Channel = Channel of Uuid * string // TODO replace string with Channel type

/// Channel actor protocol
type ChannelCtlMsg =
    | NewParticipant of user: User * subscriber: IActorRef<ChatProtocolMessage>
    | ParticipantLeft of User
    | NewMessage of User * Message
    | ListUsers

/// The message sent out to user
and MessageTs = int * DateTime

/// Client protocol message (messages sent from channel to client actor)
and ChatProtocolMessage =
    | ChatMessage of ts: MessageTs * author: User * Message
    | Joined of ts: MessageTs * user: User * all: User seq
    | Left of ts: MessageTs * user: User * all: User seq

// TODO renames ChannelMessage, ChatClientMessage