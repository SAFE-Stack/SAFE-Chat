module UserStore

// implements users catalog + persistance
open ChatUser

type UserStoreMessage =
    | Register of UserKind * Result<UserId, string> AsyncReplyChannel
    | GetUser of UserId * RegisteredUser option AsyncReplyChannel
    | GetUsers of UserId list * (RegisteredUser list AsyncReplyChannel)
    | Unregister of UserId

type State = {
    nextId: int
    users: Map<UserId, RegisteredUser>
}

module UserIds =

    let system = UserId "sys"
    let echo = UserId "echo"

module Implementation =

    let createUser userid user = Map.add userid (RegisteredUser(userid, user))

    let initialState = {
        nextId = 100
        users = Map.empty
            |> createUser UserIds.system System
            |> createUser UserIds.echo (ChatUser.makeBot "echo")
    }

    let lookupByNick nickName (users: Map<UserId, RegisteredUser>) =
        let lookup _ (RegisteredUser (_, user)) =
            getUserNick user = nickName

        users |> Map.tryFindKey lookup

    let processMessage (state: State) =
        function
        | Register (user, chan) ->
            let userNick = getUserNick user
            match state.users |> lookupByNick userNick with
            | Some (UserId uid) ->
                sprintf "The nickname %s is already taken by user %s" userNick uid |> (Error >> chan.Reply)
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

    let storeAgent = MailboxProcessor.Start(fun inbox-> 

        let rec messageLoop (state: State) =
            async {
                let! msg = inbox.Receive()
                return! messageLoop (processMessage state msg)
            }

        messageLoop initialState )

open Implementation

let register (user: UserKind) : Result<UserId,string> Async =
    storeAgent.PostAndAsyncReply (fun chan -> Register (user, chan))

let getUser (userId: UserId) : RegisteredUser option Async =
    storeAgent.PostAndAsyncReply (fun chan -> GetUser (userId, chan))

let getUsers (userIds: UserId list) : RegisteredUser list Async =
    storeAgent.PostAndAsyncReply (fun chan -> GetUsers (userIds, chan))

