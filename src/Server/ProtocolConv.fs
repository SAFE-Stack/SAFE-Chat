module ProtocolConv

open System
open ChatTypes
open ChatUser
open ChatServer

open FsChat

let (|IsChannelId|_|) = 
    Int32.TryParse >> function
    | true, value -> Some (ChannelId value)
    | _ -> None

let mapChannelId (ChannelId id) = id.ToString()

let makeBlankUserInfo userid nick :Protocol.ChanUserInfo =
    {id = userid; nick = nick; isbot = false; status = ""; email = null; imageUrl = null}

let mapUserToProtocol (RegisteredUser (UserId userid, userInfo)) :Protocol.ChanUserInfo =

    let tostr = Option.defaultValue ""
    in
    {id = userid; nick = userInfo.nick; isbot = false; status = tostr userInfo.status; email = ""; imageUrl = tostr userInfo.imageUrl} : Protocol.ChanUserInfo
    |>
    match userInfo.identity with
    | Person { email = email } ->
        fun u -> {u with email = Option.toObj email}
    | Bot ->
        fun u -> {u with isbot = true}
    | Anonymous _ ->
        id
    | System ->
        fun u -> {u with imageUrl = "/system.png"} // {makeBlankUserInfo userid "system" with imageUrl = "/system.png" }

let mapChanInfo ({name = name; topic = topic; cid = cid}: ChannelData) : Protocol.ChannelInfo =
    {id = mapChannelId cid; name = name; topic = topic; userCount = 0}
