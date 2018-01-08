module ChatServer

open System

open Akka.Actor
open Akkling

open ChannelFlow

type UserId = UserId of string
type UserInfo = {id: UserId; nick: string; status: string; email: string option; imageUrl: string option}
with static member Empty = {id = UserId ""; nick = ""; status = ""; email = None; imageUrl = None}

type ChatUser =
    User of UserInfo
    | Bot of UserInfo
    | System of UserId
with
    static member MakeUser(nick) = User {UserInfo.Empty with id = UserId nick; nick = nick}
    static member MakeBot(nick)  =  Bot {UserInfo.Empty with id = UserId nick; nick = nick}

let getUserId = function
    | User {id = id}
    | Bot {id = id}
    | System id -> id

type GetUser = UserId -> ChatUser option Async

type Message = Message of string

/// Channel is a primary store for channel info and data
type ChannelData = {
    id: int
    name: string
    topic: string
    channelActor: ChannelMessage<UserId, Message> IActorRef
}

and UserSessionData = {
    user: ChatUser
    notifySink: ServerNotifyMessage IActorRef
}

and ServerData = {
    channels: ChannelData list
    sessions: Map<UserId, UserSessionData>
}

// notification message sent to a subscribers via notify method
and ServerNotifyMessage =
    | AddChannel of ChannelData
    | DropChannel of ChannelData

/// Server protocol
type ServerControlMessage =
    | UpdateState of (ServerData -> ServerData)
    | FindChannel of (ChannelData -> bool)
    | GetOrCreateChannel of name: string
    | ListChannels of (ChannelData -> bool)

    | Subscribe of ChatUser * IActorRef<ServerNotifyMessage>
    | Unsubscribe of UserId
    | GetUsers of UserId list

type ServerReplyMessage =
    | Done
    | RequestError of string
    | FoundChannel of ChannelData
    | FoundChannels of ChannelData list
    | FoundUsers of ChatUser list

type ServerT = IActorRef<ServerControlMessage>

module internal Helpers =

    let updateChannel f chanId serverState: ServerData =
        let f chan = if chan.id = chanId then f chan else chan
        in
        {serverState with channels = serverState.channels |> List.map f}

    let byChanName name c = (c:ChannelData).name = name

    // verifies the name is correct
    let isValidName (name: string) =
        (String.length name) > 0 && Char.IsLetter name.[0]

    let __lastid = ref 100
    let newId () = System.Threading.Interlocked.Increment __lastid

module ServerApi =
    open Helpers

    /// Creates a new channel or returns existing if channel already exists
    let addChannel createChannel name topic (state: ServerData) =
        match state.channels |> List.tryFind (byChanName name) with
        | Some chan ->
            Ok (state, chan)
        | _ when isValidName name ->
            let channelActor = createChannel ()
            let newChan = {
                id = newId (); name = name; topic = topic; channelActor = channelActor }

            do state.sessions |> Map.iter(fun _ session -> session.notifySink <! AddChannel newChan)
            Ok ({state with channels = newChan::state.channels}, newChan)
        | _ ->
            Error "Invalid channel name"

    let addSub user notifySink (state: ServerData) =
        let userId = getUserId user
        { state with sessions = state.sessions |> Map.add userId { user = user; notifySink = notifySink } }
    let dropSub userId (state: ServerData) =
        { state with sessions = state.sessions |> Map.remove userId }

    let setTopic chanId newTopic state =
        Ok (state |> updateChannel (fun chan -> {chan with topic = newTopic}) chanId)

/// Starts IRC server actor.
let startServer (system: ActorSystem) =

    let rec behavior (state: ServerData) (ctx: Actor<ServerControlMessage>) =
        let replyAndUpdate f = function
            | Ok (newState, reply) -> ctx.Sender() <! f reply; become (behavior newState ctx)
            | Error errtext -> ctx.Sender() <! RequestError errtext; ignored state

        function
        | UpdateState updater ->
            become (behavior (updater state) ctx)
        | FindChannel criteria ->
            let found = state.channels |> List.tryFind criteria
            ctx.Sender() <! (found |> function |Some chan -> FoundChannel chan |_ -> RequestError "Not found")
            ignored state

        | GetOrCreateChannel name ->
            state |> ServerApi.addChannel (fun () -> createChannel system) name ""
            |> replyAndUpdate FoundChannel

        | ListChannels criteria ->
            let found = state.channels |> List.filter criteria
            ctx.Sender() <! FoundChannels found
            ignored state

        | Subscribe (user, nsink) ->
            let newState = state |> ServerApi.addSub user nsink
            become (behavior newState ctx)          

        | Unsubscribe userid ->
            let newState = state |> ServerApi.dropSub userid
            become (behavior newState ctx)          

        | GetUsers userIdList ->
            let (><) f a b = f b a
            let getUsers =
                List.collect (Map.tryFind >< state.sessions >> Option.toList)
                >> List.map (fun sessionData -> sessionData.user)
            ctx.Sender() <! FoundUsers (getUsers userIdList)
            ignored state

    in
    props <| actorOf2 (behavior { channels = []; sessions = Map.empty }) |> (spawn system "ircserver")

let private getChannelImpl message (server: ServerT) =
    async {
        let! (reply: ServerReplyMessage) = server <? message
        match reply with
        | FoundChannel channel -> return Ok channel
        | RequestError error -> return Error error
        | _ -> return Error "Unknown reason"
    }

let getChannel criteria =
    getChannelImpl (FindChannel criteria)

let getOrCreateChannel name =
    getChannelImpl (GetOrCreateChannel name)

let listChannels criteria (server: ServerT) =
    async {
        let! (reply: ServerReplyMessage) = server <? (ListChannels criteria)
        match reply with
        | FoundChannels channels -> return Ok channels
        | _ -> return Error "Unknown error"
    }

let getUsersInfo userIdList (server: ServerT) =
    async {
        let! (reply: ServerReplyMessage) = server <? (GetUsers userIdList)
        return
            match reply with
            | FoundUsers list -> list
            | _ -> [] // Error "Unknown reply"
    }

let getUser userid (server: ServerT) =
    async {
        let! (reply: ServerReplyMessage) = server <? (GetUsers [userid])
        return
            match reply with
            | FoundUsers [user] -> Some user
            | _ -> None
    }

let createTestChannels system (server: ServerT) =
    let addChannel name topic state =
        match state |> ServerApi.addChannel (fun () -> createChannel system) name topic with
        | Ok (newstate, _) -> newstate
        | _ -> state

    let addChannels =
        addChannel "Test" "test channel #1"
        >> addChannel "Weather" "join channel to get updated"

    ignore (server <? UpdateState addChannels)

let subscribeNotify (server: ServerT) user (actor: IActorRef<ServerNotifyMessage>) =
    server <! Subscribe (user, actor)
