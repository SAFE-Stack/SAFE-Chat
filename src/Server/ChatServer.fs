module ChatServer

open System

open Akka.Actor
open Akkling
open Suave.Logging

open ChatUser
open ChannelFlow

let private logger = Log.create "chatserver"

type Message = Message of string
type ChannelId = ChannelId of int

/// Channel is a primary store for channel info and data
type ChannelData = {
    id: ChannelId
    name: string
    topic: string
    channelActor: ChannelMessage<UserId, Message> IActorRef
}
and UserSessionData = {
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

    | StartSession of UserId * IActorRef<ServerNotifyMessage>
    | CloseSession of UserId

type ServerReplyMessage =
    | Done
    | RequestError of string
    | FoundChannel of ChannelData
    | FoundChannels of ChannelData list

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
                id = ChannelId (newId()); name = name; topic = topic; channelActor = channelActor }

            do state.sessions |> Map.iter(fun _ session -> session.notifySink <! AddChannel newChan)
            Ok ({state with channels = newChan::state.channels}, newChan)
        | _ ->
            Result.Error "Invalid channel name"

    let addSub userId notifySink (state: ServerData) =
        { state with sessions = state.sessions |> Map.add userId { notifySink = notifySink } }
    let dropSub userId (state: ServerData) =
        { state with sessions = state.sessions |> Map.remove userId }

    let setTopic chanId newTopic state =
        Ok (state |> updateChannel (fun chan -> {chan with topic = newTopic}) chanId)

/// Starts IRC server actor.
let startServer (system: ActorSystem) =

    let rec behavior (state: ServerData) (ctx: Actor<ServerControlMessage>) =
        let replyAndUpdate f = function
            | Ok (newState, reply) -> ctx.Sender() <! f reply; become (behavior newState ctx)
            | Result.Error errtext -> ctx.Sender() <! RequestError errtext; ignored state

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

        | StartSession (user, nsink) ->
            let newState = state |> ServerApi.addSub user nsink
            become (behavior newState ctx)          

        | CloseSession userid ->
            let newState = state |> ServerApi.dropSub userid
            become (behavior newState ctx)          
    in
    props <| actorOf2 (behavior { channels = []; sessions = Map.empty }) |> (spawn system "ircserver")

let private getChannelImpl message (server: ServerT) =
    async {
        let! (reply: ServerReplyMessage) = server <? message
        match reply with
        | FoundChannel channel -> return Ok channel
        | RequestError error -> return Result.Error error
        | _ -> return Result.Error "Unknown reason"
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
        | _ -> return Result.Error "Unknown error"
    }

let createTestChannels system (server: ServerT) =
    let addChannel name topic state =
        match state |> ServerApi.addChannel (fun () -> createChannel system) name topic with
        | Ok (newstate, _) ->
            do logger.info (Message.eventX "Added test channel '{name}'" >> Message.setFieldValue "name" name)
            newstate
        | Result.Error e ->
            do logger.error (Message.eventX "Failed to addChannel '{name}': {e}" >> Message.setFieldValue "name" name  >> Message.setFieldValue "e" e)
            state

    let addChannels =
        addChannel "Test" "test channel #1"
        >> addChannel "Weather" "join channel to get updated"

    ignore (server <! UpdateState addChannels)

let startSession (server: ServerT) userId (actor: IActorRef<ServerNotifyMessage>) =
    server <! StartSession (userId, actor)
