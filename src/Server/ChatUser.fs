module ChatUser

type UserId = UserId of string
type UserInfo = {id: UserId; nick: string; status: string; email: string option; imageUrl: string option}
with static member Empty = {id = UserId ""; nick = ""; status = ""; email = None; imageUrl = None}

type ChatUser =
    User of UserInfo
    | Bot of UserInfo
    | System of UserId

let getUserId = function
    | User {id = id}
    | Bot {id = id}
    | System id -> id
let makeUser nick = User {UserInfo.Empty with id = UserId nick; nick = nick}
let makeBot nick  =  Bot {UserInfo.Empty with id = UserId nick; nick = nick}

type GetUser = UserId -> ChatUser option Async
