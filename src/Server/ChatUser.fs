module ChatUser

open ChatTypes

type PersonalInfo = {oauthId: string option; name: string option; email: string option}

type Identity =
    | Person of PersonalInfo
    | Bot
    | Anonymous of string
    | System

// keeps information about chat user
type UserInfo = {
    identity: Identity
    nick: string
    status: string option
    imageUrl: string option
    channelList: ChannelId list
}

type RegisteredUser = RegisteredUser of UserId * UserInfo

let makeNew identity nick = {identity = identity; nick = nick; status = None; imageUrl = None; channelList = []}

let makeUserImageUrl deflt = // FIXME find the place for the method
    let computeMd5 (text: string) =
        use md5 = System.Security.Cryptography.MD5.Create()
        let hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))
        System.BitConverter.ToString(hash).Replace("-", "").ToLower()

    function
    | null | "" -> None
    | name -> name |> (computeMd5 >> sprintf "https://www.gravatar.com/avatar/%s?d=%s" >< deflt >> Some)

let getUserNick (RegisteredUser (_,{nick = nick})) = nick
let getUserId (RegisteredUser (userId,_)) = userId

type GetUser = UserId -> UserInfo option Async
