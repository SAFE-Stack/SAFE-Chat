module ChatServer

open System

open Akka.Actor
open Akkling
open Akkling.Persistence

open Suave.Logging

open ChatTypes

let private logger = Log.create "chatserver"

module public Impl =
    // move internals here from below types definition
    ()

/// Channel is a primary store for channel info and data
type ChannelData = {
    cid: ChannelId
    name: string
    topic: string
    channelActor: IActorRef<ChannelMessage>
}
and UserSessionData = {
    notifySink: ServerNotifyMessage IActorRef
}

and State = {
    channels: ChannelData list  // TODO consider map too
    sessions: Map<UserId, UserSessionData>
    lastChannelId: int
}

// notification message sent to a subscribers via notify method
and ServerNotifyMessage =
    | AddChannel of ChannelData
    | DropChannel of ChannelData

// Chan event type. I need this type to create proper actor while restoring channels
// Consider omitting parameters and just let channel store its state/settings.
type ChannelType =
    | GroupChatChannel of GroupChatFlow.ChannelConfig
    | OtherChannel of Props<ChannelMessage>     // this is not persistable channel (such as About)

type ChannelCreateInfo = {
    chanId: string
    name: string
    topic: string
    chanType: ChannelType
}

type ServerEvent =
    | ChannelCreated of ChannelCreateInfo
    | ChannelDeleted of ChannelId

/// Server protocol
type ServerCommand =
    | FindChannel of (ChannelData -> bool)
    | GetOrCreateChannel of name: string * topic: string * ChannelType  // FIXME type instead of tuple
    | ListChannels of (ChannelData -> bool)

    | StartSession of UserId * IActorRef<ServerNotifyMessage>
    | CloseSession of UserId

type ServerReplyMessage =
    | Done
    | RequestError of string
    | FoundChannel of ChannelData
    | CreatedChannel of ChannelId
    | FoundChannels of ChannelData list

type ServerMessage =
    | Event of ServerEvent
    | Command of ServerCommand

type ServerT = IActorRef<ServerMessage>

let private initialState = { channels = []; sessions = Map.empty; lastChannelId = 100 }

module private Implementation =

    let updateChannel f chanId serverState =
        let f (chan: ChannelData) = if chan.cid = chanId then f chan else chan
        in
        {serverState with channels = serverState.channels |> List.map f}

    /// Creates a new channel or returns existing if channel already exists

let startServer (system: ActorSystem) : IActorRef<ServerMessage> =

    let getChannelProps = function
        | GroupChatChannel config -> GroupChatFlow.createActorProps config
        | OtherChannel props -> props

    let byChanName name c = (c:ChannelData).name = name

    // verifies the name is correct
    let isValidName (name: string) =
        (String.length name) > 0 && Char.IsLetter name.[0]

    let serverBehavior (ctx: Eventsourced<obj>) =

        let update (state: State) = function
            | ChannelCreated ci when state.channels |> List.exists(fun {cid = ChannelId cid} -> string cid = ci.chanId) ->

                do logger.error (Message.eventX "Channel named {a} (id={chanid}) already exists, cannot restore"
                    >> Message.setFieldValue "a" ci.name >> Message.setFieldValue "chanid" ci.chanId)

                state

            | ChannelCreated ci ->

                let actorName = ci.chanId
                let actor = spawn ctx actorName (getChannelProps ci.chanType)
                let (true, chanId) | OtherwiseFail chanId = System.Int32.TryParse ci.chanId

                let newChan = {cid = ChannelId chanId; name = ci.name; topic = ci.topic; channelActor = actor }
                let newState = 
                    { state with lastChannelId = max chanId state.lastChannelId; channels = newChan::state.channels }

                do logger.debug (Message.eventX "Started watching {chanName}" >> Message.setFieldValue "chanName" ci.name)
                monitor ctx actor |> ignore

                do state.sessions |> Map.iter(fun _ session -> session.notifySink <! AddChannel newChan)

                newState

            | ChannelDeleted channelId ->
                { state with channels = state.channels |> List.filter (fun chand -> chand.cid <> channelId)}

        let rec loop (state: State) : Effect<obj> = actor {
            let! cmd = ctx.Receive()

            match cmd with
            | Terminated(ref, _, _) ->
                match state.channels |> List.tryFind (fun chan -> chan.channelActor = ref) with
                | Some channel ->
                    do state.sessions |> Map.iter(fun _ session -> session.notifySink <! DropChannel channel)
                    return ChannelDeleted channel.cid |> (Event >> box >> Persist)
                | _ ->
                    do logger.error (Message.eventX "Failed to locate terminated object: {a}" >> Message.setFieldValue "a" ref)
                    return loop state

            | :? ServerMessage as msg ->
                match msg with
                | Event evt ->
                    return loop (update state evt)

                | Command (FindChannel criteria) ->
                    let found = state.channels |> List.tryFind criteria
                    ctx.Sender() <! (found |> function |Some chan -> FoundChannel chan |_ -> RequestError "Not found")
                    return ignored ()

                | Command (GetOrCreateChannel (name, topic, channelType)) ->
                    match state.channels |> List.tryFind (byChanName name) with
                    | Some chan ->
                        ctx.Sender() <! FoundChannel chan
                        return loop state
                    | _ when isValidName name ->
                        let newChannelId = state.lastChannelId + 1
                        let event = ChannelCreated { chanId = string newChannelId; name = name; topic = topic; chanType = channelType }

                        ctx.Sender() <! CreatedChannel (ChannelId newChannelId)
                        // only persist regular channels which we know how to instantiate (persist)
                        match channelType with
                        | GroupChatChannel _ ->
                            return Persist (Event event |> box)
                        | _ ->
                            return loop <| update state event

                    | _ ->
                        ctx.Sender() <! RequestError "Invalid channel name"
                        return loop state

                | Command (ListChannels criteria) ->
                    let found = state.channels |> List.filter criteria
                    ctx.Sender() <! FoundChannels found
                    return ignored()

                | Command (StartSession (user, nsink)) ->
                    do logger.debug (Message.eventX "StartSession user={userId}" >> Message.setFieldValue "userId" user)

                    let newState = { state with sessions = state.sessions |> Map.add user { notifySink = nsink } }
                    return loop newState

                | Command (CloseSession userid) ->
                    do logger.debug (Message.eventX "CloseSession user={userId}" >> Message.setFieldValue "userId" userid)
                    
                    let newState = { state with sessions = state.sessions |> Map.remove userid }
                    return loop newState
            | msg ->
                do logger.debug (Message.eventX "Didn't process message: {a}" >> Message.setFieldValue "a" msg)
                // TODO unhandled()
                return loop state
        }
        loop initialState
    in

    let props = propsPersist serverBehavior
    spawn system "ircserver" props |> retype


let getChannel criteria (server: ServerT) =
    async {
        let! (reply: ServerReplyMessage) = server <? Command(FindChannel criteria)
        match reply with
        | FoundChannel channel -> return Ok channel
        | RequestError error -> return Result.Error error
        | _ -> return Result.Error "Unknown reason"
    }

let getOrCreateChannel name topic (channelType: ChannelType) (server: ServerT) =
    async {
        let! (reply: ServerReplyMessage) = server <? Command (GetOrCreateChannel (name, topic, channelType))
        match reply with
        | CreatedChannel channelId -> return Ok channelId
        | FoundChannel channel -> return Ok channel.cid
        | RequestError error -> return Result.Error error
        | _ -> return Result.Error "Unknown reason"
    }

let listChannels criteria (server: ServerT) =
    async {
        let! (reply: ServerReplyMessage) = server <? Command (ListChannels criteria)
        match reply with
        | FoundChannels channels -> return Ok channels
        | _ -> return Result.Error "Unknown error"
    }

let startSession (server: ServerT) userId (actor: IActorRef<ServerNotifyMessage>) =
    server <! Command (StartSession (userId, actor))
