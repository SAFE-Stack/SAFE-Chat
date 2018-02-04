module UserStore

// implements users catalog + persistance
open ChatUser

type UserStoreMessage =
    | Register of UserKind * Result<UserId, string> AsyncReplyChannel
    | GetUser of UserId * RegisteredUser option AsyncReplyChannel
    | GetUsers of UserId list * (RegisteredUser list AsyncReplyChannel)
    | Unregister of UserId
    | Update of RegisteredUser * Result<RegisteredUser, string> AsyncReplyChannel

type State = {
    nextId: int
    users: Map<UserId, RegisteredUser>
}

module UserIds =

    let system = UserId "sys"
    let echo = UserId "echo"

module private Implementation =

    let createUser userid user = Map.add userid (RegisteredUser(userid, user))
    let makeBot nick = Bot {ChatUser.empty with nick = nick; imageUrl = makeUserImageUrl "robohash" "echobott"}

    let initialState = {
        nextId = 100
        users = Map.empty
            |> createUser UserIds.system System
            |> createUser UserIds.echo (makeBot "echo")
    }

    let lookupNick nickName _ user =
        getUserNick user = nickName

    let updateUserKind =
        function
        | Anonymous _, Anonymous n -> Ok <| Anonymous n
        | Person p, Person n -> Ok <| Person {n with oauthId = p.oauthId}
        | Bot p, Bot n -> Ok <| Bot {n with oauthId = p.oauthId} // id cannot be overwritten
        | _ -> Result.Error <| "Cannot update user because of different type"

    let updateUser (RegisteredUser (userid, newuser) as uxx) (users: Map<UserId, RegisteredUser>) =
        let newNick = getUserNick uxx
        match users |> Map.tryFindKey (lookupNick newNick) with
        | Some foundUserId when foundUserId <> userid ->
            Result.Error <| "Updated nick was already taken by other user"
        | _ ->
            match users |> Map.tryFind userid with
            | Some (RegisteredUser (_, user)) -> updateUserKind (user, newuser)
            | _ -> Result.Error <| "User not found, nothing to update"
            |> Result.map(fun u ->
                let newUser = RegisteredUser (userid, u)
                newUser, users |> Map.add userid newUser )

    // in case user is logging anonymously check he cannot squote someone's nick
    let (|AnonymousBusyNick|_|) (users: Map<UserId, RegisteredUser>) =
        function
        | Anonymous {nick = userNick} ->
            users |> Map.tryFindKey (lookupNick userNick) |> Option.map(fun uid -> uid, userNick)
        | _ -> None

    let (|AlreadyRegisteredOAuth|_|) (users: Map<UserId, RegisteredUser>) =
        let lookup oauthIdArg _ = function
            | (RegisteredUser (_, Person {oauthId = Some probe})) -> probe = oauthIdArg
            | _ -> false

        function
        | Person {oauthId = Some oauthId} ->
            users |> Map.tryFindKey (lookup oauthId)
        | _ -> None

    let processMessage (state: State) =
        function

        | Register (user, chan) ->
            match user with
            | AnonymousBusyNick state.users (UserId uid, nickname) ->
                sprintf "The nickname %s is already taken by user %s" nickname uid |> (Error >> chan.Reply)
                state
            | AlreadyRegisteredOAuth state.users userId ->
                Ok userId |> chan.Reply
                state
            | _ ->
                let userId = UserId <| state.nextId.ToString()
                Ok userId |> chan.Reply
                {state with nextId = state.nextId + 1; users = state.users |> Map.add userId (RegisteredUser (userId, user))}

        | GetUser (userId, chan) ->
            state.users |> Map.tryFind userId |> chan.Reply
            state
        | GetUsers (userids, chan) ->
            userids |> List.collect (Map.tryFind >< state.users >> Option.toList) |> chan.Reply
            state
        | Unregister userid ->
            {state with users = state.users |> Map.remove userid}

        | Update (user, chan) ->
            match state.users |> updateUser user with
            | Ok(newUser, newState) ->
                Ok newUser |> chan.Reply
                {state with users = newState}
            | Result.Error e ->
                Result.Error e |> chan.Reply
                state

    let storeAgent = MailboxProcessor.Start(fun inbox -> 

        let rec messageLoop (state: State) =
            async {
                let! msg = inbox.Receive()
                return! messageLoop (processMessage state msg)
            }

        messageLoop initialState )

open Implementation

let register (user: UserKind) : Result<UserId,string> Async =
    storeAgent.PostAndAsyncReply (fun chan -> Register (user, chan))

let unregister userid =
    storeAgent.Post (Unregister userid)

let update (user: RegisteredUser) : Result<RegisteredUser,string> Async =
    storeAgent.PostAndAsyncReply (fun chan -> Update (user, chan))

let getUser (userId: UserId) : RegisteredUser option Async =
    storeAgent.PostAndAsyncReply (fun chan -> GetUser (userId, chan))

let getUsers (userIds: UserId list) : RegisteredUser list Async =
    storeAgent.PostAndAsyncReply (fun chan -> GetUsers (userIds, chan))

