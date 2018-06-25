module UserStore
// implements users catalog + persistance

open Akkling
open Akkling.Persistence

open ChatTypes
open ChatUser

module UserIds =

    let system = UserId "sys"
    let echo = UserId "echo"

module public Implementation =

    type ErrorInfo = ErrorInfo of string

    type UpdateChannelInfo =
        | Joined of ChannelId
        | Left of ChannelId

    type StoreCommand =
        | Register of UserInfo
        | Unregister of UserId
        | Update of UserId * UserInfo
        | UpdateUserChannels of UserId * UpdateChannelInfo
        | GetUsers of UserId list

    type StoreEvent =
        | AddUser of RegisteredUser
        | DropUser of UserId
        | UpdateUser of RegisteredUser
        | JoinedChannel of UserId * ChannelId
        | LeftChannel of UserId * ChannelId

    type ReplyMessage =
        | RegisterResult of Result<RegisteredUser, ErrorInfo>
        | UpdateResult of Result<RegisteredUser, ErrorInfo>
        | GetUsersResult of RegisteredUser list

    type StoreMessage =
        | Event of StoreEvent
        | Command of StoreCommand

    type State = {
        nextId: int
        users: Map<UserId, UserInfo>
    }

    let makeUser nick identity = {identity = identity; nick = nick; status = None; imageUrl = None; channelList = []}
    let makeBot nick = {makeUser nick Bot with imageUrl = makeUserImageUrl "robohash" "echobott"}

    let initialState = {
        nextId = 100
        users = Map.empty
            |> Map.add UserIds.system (makeUser "system" System)
            |> Map.add UserIds.echo (makeBot "echo")
    }

    let lookupNick nickName _ (userInfo: UserInfo) =
        userInfo.nick = nickName

    // TODO UserInfo is overkill here.
    let updateUser userid newuser (users: Map<UserId, UserInfo>) : Result<_,ErrorInfo> =
        let newNick = newuser.nick
        match users |> Map.tryFindKey (lookupNick newNick) with
        | Some foundUserId when foundUserId <> userid ->
            Result.Error <| ErrorInfo "Updated nick was already taken by other user"
        | _ ->
            match users |> Map.tryFind userid with
            | Some user -> (userid, {user with nick = newuser.nick; status = newuser.status; imageUrl = newuser.imageUrl}) |> (RegisteredUser >> Ok)
            | _ -> Result.Error <| ErrorInfo "User not found, nothing to update"

    // in case user is logging anonymously check he cannot squote someone's nick
    let (|AnonymousBusyNick|_|) (users: Map<UserId, UserInfo>) =
        function
        | Anonymous userNick ->
            users |> Map.tryFindKey (lookupNick userNick) |> Option.map(fun uid -> uid, userNick)
        | _ -> None

    let (|AlreadyRegisteredOAuth|_|) (users: Map<UserId, UserInfo>) =
        let lookup oauthIdArg _ value =
            match value with
            | {identity = Person {oauthId = Some probe}} -> probe = oauthIdArg
            | _ -> false

        function
        | Person {oauthId = Some oauthId} ->
            users |> Map.tryFindKey (lookup oauthId)
            |> Option.map (fun userId -> RegisteredUser (userId, users.[userId]))
        | _ -> None

    let updateUserInfo userId f (state: State) =
        let updatedUser =
            match state.users |> Map.tryFind userId with
            | Some userInfo -> f userInfo
            | None ->
                failwith <| sprintf "no user with id=%A for update" userId
        {state with users = state.users |> Map.add userId updatedUser}

    // processes the event and updates store
    // this is the only way to update users
    let update (state: State) = function
        | AddUser (RegisteredUser (userId, userInfo)) ->
            let (UserId uidstr) = userId
            let lastId =
                match System.Int32.TryParse uidstr with
                | true, num -> num
                | _ -> 0
            {state with nextId = (max state.nextId lastId) + 1; users = state.users |> Map.add userId userInfo}

        | DropUser userid ->
            {state with users = state.users |> Map.remove userid}

        | UpdateUser (RegisteredUser (userId, userInfo)) ->
            state |> updateUserInfo userId (fun _ -> userInfo)

        | JoinedChannel (userId, chanId) ->
            state |> updateUserInfo userId (fun userInfo -> {userInfo with channelList = chanId :: userInfo.channelList})

        | LeftChannel (userId, chanId) ->
            state |> updateUserInfo userId (fun userInfo -> {userInfo with channelList = userInfo.channelList |> List.except [chanId]})

    let handler (ctx: Eventsourced<_>) =
        let reply m = ctx.Sender() <! m

        let rec loop (state: State) = actor {
            let! msg = ctx.Receive()
            match msg with
            | Event e ->
                return! loop (update state e)
            | Command cmd ->
                match cmd with
                | Register (user) ->
                    match user.identity with
                    | AnonymousBusyNick state.users (UserId uid, nickname) ->
                        let errorMessage = sprintf "The nickname %s is already taken by user %s" nickname uid
                        ErrorInfo errorMessage |> (Result.Error >> RegisterResult >> reply)
                        return loop state
                    | AlreadyRegisteredOAuth state.users user ->
                        user |> (Ok >> RegisterResult >> reply)
                        return loop state
                    | _ ->
                        let userId = UserId <| state.nextId.ToString()
                        let newUser = RegisteredUser (userId, user)
                        newUser |> (Ok >> RegisterResult >> reply)
                        return newUser |> (AddUser >> Event >> Persist)

                | Unregister userid ->
                    return Persist (Event <| DropUser userid)

                | Update (userId, user) ->
                    match state.users |> updateUser userId user with
                    | Ok newUser ->
                        newUser |> (Ok >> UpdateResult >> reply)
                        return Persist (Event <| UpdateUser newUser)
                    | Result.Error e ->
                        e |> (Result.Error >> UpdateResult >> reply)
                        return loop state
                | UpdateUserChannels (userId, update) ->
                    return update |> (
                        function
                        | UpdateChannelInfo.Joined chanId ->
                            JoinedChannel (userId, chanId)
                        | UpdateChannelInfo.Left chanId ->
                            LeftChannel (userId, chanId)
                        >> Event >> Persist)

                | GetUsers (userids) ->
                    let findUser userId = Map.tryFind userId state.users |> Option.map (fun u -> RegisteredUser (userId, u))
                    userids |> List.collect (findUser >> Option.toList)
                            |> (GetUsersResult >> reply)
                    return loop state
        }
        loop initialState

open Implementation

type UserStore(system: Akka.Actor.ActorSystem) =

    let storeActor = spawn system "userstore" <| propsPersist(handler)

    member __.Register(user: UserInfo) : Result<RegisteredUser,string> Async =
        async {
            let! (RegisterResult result | OtherwiseFail result) = storeActor <? Command(Register user)
            return result |> Result.mapError (fun (ErrorInfo error) -> error)
        }

    member __.Unregister (userid: UserId) : unit =
        storeActor <! (Command <| Unregister userid)

    member __.Update(userId, user) : Result<RegisteredUser,string> Async =
        async {
            let! (UpdateResult result | OtherwiseFail result) = storeActor <? (Command <| Update (userId, user))
            return result |> Result.mapError (fun (ErrorInfo error) -> error)
        }

    member __.GetUser userid : UserInfo option Async =
        async {
            let! (GetUsersResult result | OtherwiseFail result) = storeActor <? (Command <| GetUsers [userid])
            return result |> function |[] -> None | (RegisteredUser (_, userInfo))::_ -> Some userInfo
        }

    member __.GetUsers (userids: UserId list) : RegisteredUser list Async =
        async {
            let! (GetUsersResult result | OtherwiseFailErr "no choice" result) = storeActor <? (Command <| GetUsers userids)
            return result
        }

    member __.UpdateUserChannel(userId: UserId, update: UpdateChannelInfo) =
        storeActor <! (Command <| UpdateUserChannels (userId, update))
