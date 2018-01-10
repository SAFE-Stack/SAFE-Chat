module ChatUser

type UserId = UserId of string

type PersonalInfo = {nick: string; status: string; email: string option; imageUrl: string option}
type AnonymousUserInfo = {nick: string; status: string}

type UserKind =
| Person of PersonalInfo
| Bot of PersonalInfo
| Anonymous of AnonymousUserInfo
| System

type RegisteredUser = RegisteredUser of UserId * UserKind

let getUserId (RegisteredUser (userid,_)) = userid

let private empty = {nick = ""; status = ""; email = None; imageUrl = None}

let makeUser nick = Person {empty with nick = nick}
let makeBot nick  =  Bot {empty with nick = nick}

type GetUser = UserId -> RegisteredUser option Async
