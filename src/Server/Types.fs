module Types

open System
open Akkling

let mutable private __seq = 100000

type Uuid = {i1: int}
with
    static member Empty = {i1 = -1}
    static member New() =
        __seq <- __seq + 1    // fixme, Interlocked.Increment
        {i1 = __seq}
    override this.ToString() =
        this.i1.ToString("x")

type UserInfo = {
    id: Uuid
    nick: string
    email: string option
} with static member Blank = {id = Uuid.Empty; nick = null; email = None}
type User = User of UserInfo
type Message = Message of string

type ChannelInfo = {
    name: string
    topic: string
}
