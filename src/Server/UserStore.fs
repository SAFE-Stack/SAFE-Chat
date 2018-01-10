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

let processMessage (state: State) =
    function
    | Register (user, chan) ->
        let userId = UserId <| state.nextId.ToString()
        // TODO check user with the same nick exists
        match state.users |> Map.containsKey userId with
        | true ->
            Error "User already registered" |> chan.Reply
            state
        | false ->
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

    let rec messageLoop (state: State) = async{
        let! msg = inbox.Receive()
        return! messageLoop (processMessage state msg)
        }

    // start the loop 
    messageLoop {nextId = 100; users = Map.empty} )