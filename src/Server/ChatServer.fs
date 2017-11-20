module ChatServer

open System

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl
open Akkling

open ChannelFlow
open FsChat

type UserNick = UserNick of string
type MaterializeFlow = Uuid -> Flow<Message, UserNick ChatClientMessage, Akka.NotUsed> -> UniqueKillSwitch

// TODO user Protocol instead
type ChannelInfo = {id: Uuid; name: string; topic: string; userCount: int; users: UserNick list}
type UserInfo = {nick: UserNick; name: string; email: string option; channels: ChannelInfo list}

let getNickname (UserNick nick) = nick

module ServerState =

    type UserData = {
        nick: UserNick
        name: string
        euid: string option
        email: string option
        mat: MaterializeFlow option
        channels: Map<Uuid, UniqueKillSwitch option>
    }

    /// Channel is a primary store for channel info and data
    type ChannelData = {
        id: Uuid
        name: string
        topic: string
        channelActor: IActorRef<UserNick ChannelMessage>
    }

    type ServerData = {
        channels: ChannelData list
        users: UserData list
    }

type ServerControlMessage =
    | ServerMessage of requestId: string * UserNick * Protocol.ServerMsg

    // TODO remove the rest
    | List                                              // returns ChannelList
    | NewChannel of name: string * topic: string        // returns ChannelInfo
    | SetTopic of chan: Uuid * topic: string
    // RenameChan
    | FindChannel of name: string                       // return ChannelInfo
    | DropChannel of chan: Uuid
    // user specific commands
    | Register of nick: UserNick * name: string * euid: string option * channels: Uuid list    // return UserInfo
    | Unregister of UserNick
    | Connect of UserNick * mat: MaterializeFlow
    | Disconnect of UserNick
    | GetUser of UserNick                             // returns UserInfo

    | UpdateState of (ServerState.ServerData -> ServerState.ServerData)
    | ReadState

type ServerReplyMessage =
    | ClientMessage of Protocol.ClientMsg
    | Done
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
    let byUserNick nick u = (u:UserData).nick = nick

    let setChannelTopic topic (chan: ChannelData) =
        {chan with topic = topic}

    let updateUser f userNick serverState: ServerData =
        let u (user: UserData) = if user.nick = userNick then f user else user
        in
        updateUsers u serverState

    let addUserChan chanId ks (user: UserData) =
        {user with channels = user.channels |> Map.add chanId ks}

    let disconnect (user: UserData) =
        {user with
            mat = None
            channels = user.channels |> Map.map (fun _ ks ->
                match ks with
                | Some killSwitch -> killSwitch.Shutdown()
                | _ -> ()
                None)
        }

    let alreadyJoined channels selectChan (u: UserData) =
        channels |> List.tryFind selectChan
        |> function
        | Some ch when u.channels |> Map.containsKey ch.id -> true
        | _ -> false

    let leaveChan chanId (user: UserData) =
        {user with channels = user.channels |> Map.remove chanId}
    
    let getChannelInfo (data: ChannelData) =
        async {
            let! (users: UserNick list) = data.channelActor <? ListUsers
            return {id = data.id; name = data.name; topic = data.topic; userCount = users |> List.length; users = users}
        }
    let getChannelInfo0 (data: ChannelData) =
        {id = data.id; name = data.name; topic = data.topic; userCount = 0; users = []}

    let getUserInfo (channels: ChannelData list) (data: UserData) =
        let getChan ids =
            channels |> List.filter (fun chan -> ids |> Map.containsKey chan.id)
            |> List.map getChannelInfo0 // FIXME does not return userCount
        { nick = data.nick; name = data.name; email = data.email; channels = data.channels |> getChan}

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
    let addChannel createChannel name topic (state: ServerData) =
        match state.channels |> List.tryFind (byChanName name) with
        | Some chan ->
            Ok (state, chan)
        | _ when isValidName name ->
            let channelActor = createChannel name
            let newChan = {
                id = Uuid.New(); name = name; topic = topic; channelActor = channelActor }
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

    let private userOp userNick state =
        state.users |> List.tryFind (byUserNick userNick) |> function
        | None      -> Result.Error "User with such id not found"
        | Some user -> Ok user

    // Turns user to an "online" mode, and user starts receiving messages from all channels he's subscribed
    let connect userNick (mat: MaterializeFlow) state =
        userOp userNick state
        |> Result.bind(function
            | user when user.mat |> Option.isSome ->
                Result.Error "User already connected"
            | user ->
                let connectChannels channels =
                    state.channels
                    |> List.filter(fun chan -> channels |> Map.containsKey chan.id)
                    |> List.map (fun chan ->
                        chan.id, Some (createChannelFlow chan.channelActor userNick |> mat chan.id))
                    |> Map.ofList
                let connectUser user = { user with mat = Some mat; channels = user.channels |> connectChannels}
                Result.Ok (state |> updateUser connectUser userNick)
        )

    let register nick name euid channels state =
        match state.users |> List.exists(fun u -> u.nick = nick) with
        | true ->
            Result.Error "User with such nick already exists"
        | _ ->
            let newUser = {
                euid = euid; nick = nick; name = name; email = None; mat = None
                channels = state.channels
                    |> List.filter(fun chan -> channels |> List.contains chan.id)
                    |> List.map (fun chan -> chan.id, None)
                    |> Map.ofList
            }
            Ok ({state with users = newUser :: state.users}, getUserInfo state.channels newUser)

    let disconnect userId state =
        userOp userId state
        |> Result.map (fun _ -> state |> updateUser disconnect userId)

    // User leaves the chat server
    let unregister userNick state =
        userOp userNick state
        |> Result.map (fun _ ->
            state |> updateUser Helpers.disconnect userNick |> removeUser (byUserNick userNick))

    let joinOrCreate userId channelName createChannel state =
        userOp userId state
        |> Result.bind(function
            | user when user |> alreadyJoined state.channels (byChanName channelName) ->
                Result.Error "User already joined this channel"
            | user ->
                match addChannel createChannel channelName "/// set topic for the new channel" state with
                | Ok (newState, chan) ->
                    let ks = user.mat |> Option.map (fun m -> m chan.id <| createChannelFlow chan.channelActor userId)
                    Ok (newState |> updateUser (addUserChan chan.id ks) userId, chan)
                | Result.Error error -> Result.Error error)

    let join userId channelId state =
        userOp userId state
        |> Result.bind(function
            | user when user |> alreadyJoined state.channels (byChanId channelId) ->
                Result.Error "User already joined this channel"
            | user ->
                state.channels |> List.tryFind (byChanId channelId) |> function
                | Some chan ->
                    let ks = user.mat |> Option.map (fun m -> m chan.id <| createChannelFlow chan.channelActor userId)
                    Ok (state |> updateUser (addUserChan chan.id ks) userId, chan)
                | _ -> Result.Error "Channel not found")

    let leave userId chanId state =
        userOp userId state
        |> Result.bind (fun user ->
            match user.channels |> Map.tryFind chanId with
            | None ->
                Result.Error "User is not joined channel"
            | Some kso ->
                kso |> Option.map (fun ks -> ks.Shutdown()) |> ignore
                Ok (state |> updateUser (leaveChan chanId) userId, chanId))

    let getUserInfo userId state =
        userOp userId state
        |> Result.map (getUserInfo state.channels)

open ServerState
open Helpers

/// Starts IRC server actor.
let startServer (system: ActorSystem) =

    let rec behavior (state: ServerData) (ctx: Actor<ServerControlMessage>) =
        let passError errtext =
            ctx.Sender() <! Error errtext
            ignored state
        let passError1 reqId errtext =
            ctx.Sender() <! (Protocol.CannotProcess (reqId, errtext) |> Protocol.ClientMsg.Error)
            ignored state

        let replyAndUpdate1 reqId = function
            | Ok (newState, reply) -> ctx.Sender() <! reply; become (behavior newState ctx)
            | Result.Error errText -> passError1 reqId errText

        let reply1 reqId = function
            | Ok result -> ctx.Sender() <! result; ignored state
            | Result.Error errtext -> passError1 reqId errtext

        let update1 reqId = function
            | Ok newState -> ctx.Sender() <! Done; become (behavior newState ctx)
            | Result.Error errText -> passError1 reqId errText

        let reply = function
            | Ok result -> ctx.Sender() <! result; ignored state
            | Result.Error errtext -> passError errtext
        let update = function
            | Ok newState -> ctx.Sender() <! Done; become (behavior newState ctx)
            | Result.Error errText -> passError errText
        let replyAndUpdate = function
            | Ok (newState, reply) -> ctx.Sender() <! reply; become (behavior newState ctx)
            | Result.Error errText -> passError errText
        let mapReply f = Result.map (fun (ns, r) -> ns, f r)

        let mapChannel isMine (chan: ChannelInfo) : Protocol.ChannelInfo =
            {id = chan.id.ToString(); name = chan.name; topic = chan.topic; userCount = chan.userCount; users = []; joined = isMine chan.id}
        let mapChannelMine = mapChannel (fun _ -> true)

        function
        | ServerMessage (requestId, user, msg) ->
            match msg with
            | Protocol.ServerMsg.Join chanIdStr ->
                match Uuid.TryParse chanIdStr with
                | Some channelId ->
                    state |> ServerApi.join user channelId
                    |> mapReply (getChannelInfo0 >> mapChannelMine >> Protocol.JoinedChannel)
                    |> replyAndUpdate1 requestId
                | _ -> passError1 requestId "bad channel id"
            | Protocol.ServerMsg.JoinOrCreate channelName ->
                state |> ServerApi.joinOrCreate user channelName (createChannel system)
                |> mapReply (getChannelInfo0 >> mapChannelMine >> Protocol.JoinedChannel)
                |> replyAndUpdate1 requestId
            |  Protocol.ServerMsg.Leave chanIdStr ->
                match Uuid.TryParse chanIdStr with
                | Some channelId ->
                    state |> ServerApi.leave user channelId
                    |> mapReply (fun _ -> Protocol.ClientMsg.LeftChannel chanIdStr)
                    |> replyAndUpdate1 requestId
                | _ -> passError1 requestId "bad channel id"
            | _ -> passError1 requestId "unsupported command"
            
        | List ->
            ctx.Sender() <!| (state |> ServerApi.listChannels |> Async.map ChannelList)
            ignored state

        | NewChannel (name, topic) ->
            state |> ServerApi.addChannel (createChannel system) name topic
            |> mapReply (getChannelInfo0 >> ChannelInfo)
            |> replyAndUpdate

        | FindChannel name ->
            state |> ServerApi.findChannel name |> Result.map ChannelInfo |> reply

        | SetTopic (chanId, topic) ->   update (state |> ServerApi.setTopic chanId topic)
        | DropChannel chanId ->         update (state |> ServerApi.dropChannel chanId)

        | Register (nick, name, euid, channels) ->  state |> ServerApi.register nick name euid channels |> mapReply UserInfo |> replyAndUpdate

        | Unregister userId ->          update (state |> ServerApi.unregister userId)
        | Connect (userId, mat) ->      update (state |> ServerApi.connect userId mat)
        | Disconnect userId ->          update (state |> ServerApi.disconnect userId)

        | GetUser userNick ->
            state |> ServerApi.getUserInfo userNick
            |> Result.map UserInfo
            |> reply
        | ReadState ->
            ctx.Sender() <! state; ignored state
        | UpdateState updater ->
            become (behavior (updater state) ctx)

    in
    props <| actorOf2 (behavior { channels = []; users = [] }) |> (spawn system "ircserver")

let registerNewUser nick name euid channels (server: IActorRef<ServerControlMessage>) =
    async {
        let! (serverState: ServerState.ServerData) = server <? ReadState
        let existingUser =
            if euid |> Option.isSome then
                serverState.users |> List.tryFind (fun u -> u.euid = euid)
            else
                None

        match existingUser with
        | Some x -> return ()
        | _ ->
            let! reply = server <? Register (nick, name, euid, channels)
            return reply
                |> function
                | UserInfo _ -> ()
                | Error e -> failwith e
    }

let locateUser predicate (server: IActorRef<ServerControlMessage>) =
    async {
        let! (serverState: ServerState.ServerData) = server <? ReadState
        return serverState.users |> List.tryFind predicate
    }

