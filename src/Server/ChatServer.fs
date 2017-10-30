module ChatServer

open System

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl
open Akkling

open ChannelFlow

type MaterializeFlow = Uuid -> Flow<Message,Uuid ChatClientMessage, Akka.NotUsed> -> UniqueKillSwitch

type ChannelInfo = {id: Uuid; name: string; topic: string; userCount: int; users: Uuid list}
type UserInfo = {id: Uuid; nick: string; email: string option; channels: ChannelInfo list}

module ServerState =

    type UserData = {
        id: Uuid
        nick: string
        email: string option
        mat: MaterializeFlow option
        channels: Map<Uuid, UniqueKillSwitch option>
    }

    /// Channel is a primary store for channel info and data
    type ChannelData = {
        id: Uuid
        name: string
        topic: string
        channelActor: IActorRef<Uuid ChannelMessage>
    }

    type ServerData = {
        channels: ChannelData list
        users: UserData list
    }

type ServerControlMessage =
    | List                                              // returns ChannelList
    | NewChannel of name: string                        // returns ChannelInfo
    | SetTopic of chan: Uuid * topic: string
    // RenameChan
    | FindChannel of name: string                       // return ChannelInfo
    | DropChannel of Uuid: Uuid
    // user specific commands
    | Register of nick: string * channels: Uuid list   // return UserInfo
    | Unregister of user: Uuid
    | Connect of user: Uuid * mat: MaterializeFlow
    | Disconnect of user: Uuid
    | Join of user: Uuid * channelName: string
    // | Nick of user: Uuid * newNick: string
    | Leave of user: Uuid * chanId: Uuid
    | GetUser of user: Uuid                             // returns UserInfo

    | UpdateState of (ServerState.ServerData -> ServerState.ServerData)
    | ReadState

type ServerReplyMessage =
    | ChannelList of ChannelInfo list
    | ChannelInfo of ChannelInfo
    | UserInfo of UserInfo
    | State of ServerState.ServerData
    | Error of string

module internal Helpers =
    open ServerState

    let updateChannels f serverState: ServerData =
        {serverState with channels = serverState.channels |> List.map f}

    let updateUsers f serverState: ServerData =
        {serverState with users = serverState.users |> List.map f}

    let removeUser predicate serverState: ServerData =
        {serverState with users = serverState.users |> List.filter (predicate >> not)}

    let updateChannel f chanId serverState: ServerData =
        let u chan = if chan.id = chanId then f chan else chan
        in
        updateChannels u serverState

    let byChanId id c = (c:ChannelData).id = id
    let byChanName name c = (c:ChannelData).name = name
    let byUserId id u = (u:UserData).id = id

    let setChannelTopic topic (chan: ChannelData) =
        {chan with topic = topic}

    let updateUser f userId serverState: ServerData =
        let u (user: UserData) = if user.id = userId then f user else user
        in
        updateUsers u serverState

    let addUserChan chanId ks (user: UserData) =
        {user with channels = user.channels |> Map.add chanId ks}

    let disconnect (user: UserData) =
        {user with
            mat = None
            channels = user.channels |> Map.map (fun chanId ks ->
                match ks with
                | Some killSwitch -> killSwitch.Shutdown()
                | _ -> ()
                None)
        }

    let alreadyJoined channels channelName (u: UserData) =
        channels |> List.tryFind (byChanName channelName)
        |> function
        | Some ch when u.channels |> Map.containsKey ch.id -> true
        | _ -> false

    let leaveChan chanId (user: UserData) =
        {user with channels = user.channels |> Map.remove chanId}
    
    let getChannelInfo (data: ChannelData) =
        async {
            let! (users: Uuid list) = data.channelActor <? ListUsers
            return {id = data.id; name = data.name; topic = data.topic; userCount = users |> List.length; users = users}
        }
    let getChannelInfo0 (data: ChannelData) =
        {id = data.id; name = data.name; topic = data.topic; userCount = 0; users = []}

    let getUserInfo (channels: ChannelData list) (data: UserData) =
        let getChan ids =
            channels |> List.filter (fun chan -> ids |> Map.containsKey chan.id)
            |> List.map getChannelInfo0 // FIXME does not return userCount
        { id = data.id; nick = data.nick; email = data.email
          channels = data.channels |> getChan}

    module Async =
        let map f workflow = async {
            let! res = workflow
            return f res }

module ServerApi =
    open ServerState
    open Helpers

    // verifies the name is correct
    let isValidName (name: string) =
        (String.length name) > 0
        && Char.IsLetter name.[0]

    let listChannels state =
        async {
            let! channels = state.channels |> List.map getChannelInfo |> Async.Parallel
            return channels |> Array.toList
        }

    /// Creates a new channel or returns existing if channel already exists
    let addChannel createChannel name (state: ServerData) =
        match state.channels |> List.tryFind (byChanName name) with
        | Some chan ->
            Ok (state, chan)
        | _ when isValidName name ->
            let channelActor = createChannel name
            let newChan = {
                id = Uuid.New(); name = name; topic = ""; channelActor = channelActor }
            Ok ({state with channels = newChan::state.channels}, newChan)
        | _ ->
            Result.Error "Invalid channel name"

    let findChannel name (state: ServerData) =
        match state.channels |> List.tryFind (byChanName name) with
        | Some chan -> getChannelInfo0 chan |> Ok
        | _ -> Result.Error "Channel with such name not found"

    let findChannelEx name (state: ServerData) =
        match state.channels |> List.tryFind (byChanName name) with
        | Some chan -> getChannelInfo chan |> Async.map Ok
        | _ -> Result.Error "Channel with such name not found" |> async.Return

    let setTopic chanId newTopic state =
        Ok (state |> updateChannel (setChannelTopic newTopic) chanId)

    let private kickUser chanId (u: UserData) =
        match u.channels |> Map.tryFind chanId with
        | Some (Some ks) ->
            do ks.Shutdown()
            {u with channels = u.channels |> Map.remove chanId}
        | Some _ ->
            {u with channels = u.channels |> Map.remove chanId}
        | _ -> u

    let dropChannel chanId state =
        // TODO consider automatic dropping the channel when no users left
        // TODO drop all users from the channel
        match state.channels |> List.tryFind (byChanId chanId) with
        | Some _ ->
            let newState = state |> updateUsers (kickUser chanId)
            in
            Ok {newState with channels = state.channels |> List.filter (not << byChanId chanId)}
        | _ -> Result.Error "Channel not found"

    let private userOp userId state =
        match state.users |> List.tryFind (fun u -> u.id = userId) with
        | None ->
            Result.Error "User with such id not found"
        | Some user ->
            Ok user

    // Turns user to an "online" mode, and user starts receiving messages from all channels he's subscribed
    let connect userId (mat: MaterializeFlow) state =
        userOp userId state
        |> Result.bind(function
            | user when user.mat |> Option.isSome ->
                Result.Error "User already connected"
            | user ->
                let connectChannels channels =
                    state.channels
                    |> List.filter(fun chan -> channels |> Map.containsKey chan.id)
                    |> List.map (fun chan ->
                        chan.id, Some (createChannelFlow chan.channelActor userId |> mat chan.id))
                    |> Map.ofList
                let connectUser user = { user with mat = Some mat; channels = user.channels |> connectChannels}
                Result.Ok (state |> updateUser connectUser userId)
        )

    let register (userId: Uuid option) nick channels state =
        match state.users |> List.exists(fun u -> u.nick = nick || Some u.id = userId) with
        | true ->
            Result.Error "User with such nick (or Id) already exists"
        | _ ->
            let newUserId = userId |> Option.defaultValue (Uuid.New())
            let newUser = {
                id = newUserId; nick = nick; email = None; mat = None
                channels = state.channels
                    |> List.filter(fun chan -> channels |> List.contains chan.id)
                    |> List.map (fun chan -> chan.id, None)
                    |> Map.ofList
            }
            Ok ({state with users = newUser :: state.users}, getUserInfo state.channels newUser)

    let disconnect userId state =
        userOp userId state
        |> Result.map (fun _ -> state |> updateUser Helpers.disconnect userId)

    // User leaves the chat server
    let unregister userId state =
        userOp userId state
        |> Result.map (fun _ ->
            state |> updateUser Helpers.disconnect userId |> removeUser (byUserId userId))

    let join userId channelName createChannel state =
        userOp userId state
        |> Result.bind(function
            | user when user |> alreadyJoined state.channels channelName ->
                Result.Error "User already joined this channel"
            | user ->
                match addChannel createChannel channelName state with
                | Ok (newState, chan) ->
                    let ks = user.mat |> Option.map (fun m -> m chan.id <| createChannelFlow chan.channelActor userId)
                    Ok (newState |> updateUser (addUserChan chan.id ks) userId, chan)
                | Result.Error error -> Result.Error error)

    let leave userId chanId state =
        userOp userId state
        |> Result.bind (fun user ->
            match user.channels |> Map.tryFind chanId with
            | None ->
                Result.Error "User is not joined channel"
            | Some kso ->
                kso |> Option.map (fun ks -> ks.Shutdown()) |> ignore
                Ok (state |> updateUser (leaveChan chanId) userId))

    let getUserInfo userId state =
        userOp userId state
        |> Result.map (getUserInfo state.channels)

open ServerState
open Helpers

/// Starts IRC server actor.
let startServer (system: ActorSystem) =

    let rec behavior (state: ServerData) (ctx: Actor<ServerControlMessage>) : ServerControlMessage -> Effect<'a> =
        let passError errtext =
            ctx.Sender() <! Error errtext
            ignored state
        let reply = function
            | Ok result -> ctx.Sender() <! result; ignored state
            | Result.Error errtext -> passError errtext
        let update = function
            | Ok newState -> become (behavior newState ctx)
            | Result.Error errText -> passError errText
        let replyAndUpdate = function
            | Ok (newState, reply) -> ctx.Sender() <! reply;  become (behavior newState ctx)
            | Result.Error errText -> passError errText
        let mapReply f = Result.map (fun (ns, r) -> ns, f r)

        function
        | List ->
            ctx.Sender() <!| (state |> ServerApi.listChannels |> Async.map ChannelList)
            ignored state

        | NewChannel name ->
            state |> ServerApi.addChannel (createChannel system) name
            |> mapReply (getChannelInfo0 >> ChannelInfo)
            |> replyAndUpdate

        | FindChannel name ->
            state |> ServerApi.findChannel name |> Result.map ChannelInfo |> reply

        | SetTopic (chanId, topic) ->   update (state |> ServerApi.setTopic chanId topic)
        | DropChannel chanId ->         update (state |> ServerApi.dropChannel chanId)

        | Register (nick, channels) ->  state |> ServerApi.register None nick channels |> mapReply UserInfo |> replyAndUpdate

        | Unregister userId ->          update (state |> ServerApi.unregister userId)
        | Connect (userId, mat) ->      update (state |> ServerApi.connect userId mat)
        | Disconnect userId ->          update (state |> ServerApi.disconnect userId)

        | Join (userId, channelName) ->
            state |> ServerApi.join userId channelName (createChannel system)
            |> mapReply (getChannelInfo0 >> ChannelInfo)
            |> replyAndUpdate

        | Leave (userId, chanId) ->     update (state |> ServerApi.leave userId chanId)

        | GetUser userId ->
            state |> ServerApi.getUserInfo userId
            |> Result.map UserInfo
            |> reply
        | ReadState ->
            ctx.Sender() <! state; ignored state
        | UpdateState updater ->
            state |> updater |> ignored

    in
    props <| actorOf2 (behavior { channels = []; users = [] }) |> (spawn system "ircserver")

let registerNewUser nick channels (server: IActorRef<ServerControlMessage>) =
    async {
        let! reply = server <? Register (nick, channels)
        return reply
            |> function
            | UserInfo userInfo -> userInfo.id
            | Error e -> failwith e; Uuid.Empty // FIXME
    }

