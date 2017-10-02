module fschat.Flowr.Types

open System
open Akkling

type User = string
type Message = Message of string

type ChannelCtlMsg =
    | NewParticipant of user: User * subscriber: IActorRef<ChatProtocolMessage>
    | ParticipantLeft of user: User
    | NewMessage of user: User * message: String
    | ListUsers

/// The message sent out to user
and MessageTs = int * DateTime
and ChatProtocolMessage =
    | ChatMessage of ts: MessageTs * author: User * message: string
    | Joined of ts: MessageTs * user: User * all: User seq
    | Left of ts: MessageTs * user: User * all: User seq
