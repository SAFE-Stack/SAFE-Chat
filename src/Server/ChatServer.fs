module ChatServer

open System

open Akka.Actor
open Akkling

open ChannelFlow

type Party = {nick: string; isbot: bool}
with
    static member Make(nick) = {nick = nick; isbot = false}

/// Channel is a primary store for channel info and data
type ChannelData = {
    id: int
    name: string
    topic: string
    channelActor: IActorRef<Party ChannelMessage>
}

and ServerData = {
    channels: ChannelData list
    subscribers: Map<Party, IActorRef<ServerNotifyMessage>>
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

    | Subscribe of Party * IActorRef<ServerNotifyMessage>
    | Unsubscribe of Party

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
            let channelActor = createChannel name
            let newChan = {
                id = newId (); name = name; topic = topic; channelActor = channelActor }

            do state.subscribers |> Map.iter(fun _ actor -> actor <! AddChannel newChan)
            Ok ({state with channels = newChan::state.channels}, newChan)
        | _ ->
            Error "Invalid channel name"

    let addSub party actor (state: ServerData) =
        { state with subscribers = state.subscribers |> Map.add party actor }
    let dropSub party (state: ServerData) =
        { state with subscribers = state.subscribers |> Map.remove party }

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
            state |> ServerApi.addChannel (createChannel system) name ""
            |> replyAndUpdate FoundChannel

        | ListChannels criteria ->
            let found = state.channels |> List.filter criteria
            ctx.Sender() <! FoundChannels found
            ignored state

        | Subscribe (party, actor) ->
            let newState = state |> ServerApi.addSub party actor
            become (behavior newState ctx)          

        | Unsubscribe party ->
            let newState = state |> ServerApi.dropSub party
            become (behavior newState ctx)          

    in
    props <| actorOf2 (behavior { channels = []; subscribers = Map.empty }) |> (spawn system "ircserver")

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

let createTestChannels system (server: ServerT) =
    let addChannel name topic state =
        match state |> ServerApi.addChannel (createChannel system) name topic with
        | Ok (newstate, _) -> newstate
        | _ -> state

    let addChannels =
        addChannel "Test" "test channel #1"
        >> addChannel "Weather" "join channel to get updated"

    ignore (server <? UpdateState addChannels)

let subscribeNotify (server: ServerT) party (actor: IActorRef<ServerNotifyMessage>) =
    server <! Subscribe (party, actor)