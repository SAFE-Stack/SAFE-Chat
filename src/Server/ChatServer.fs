module ChatServer

open System

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Streams

open ChannelFlow

type MaterializeFlow = Flow<Message,Uuid ChatClientMessage, Akka.NotUsed> -> UniqueKillSwitch

type ChannelInfo = {id: Uuid; name: string; topic: string; userCount: int}
type UserInfo = {id: Uuid; nick: string; email: string option; channels: ChannelInfo list}

type ServerControlMessage =
    | List                                              // returns ChannelList
    | NewChannel of name: string                        // returns ChannelInfo
    | SetTopic of chan: Uuid * topic: string
    // RenameChan
    | FindChannel of name: string                       // return ChannelInfo
    | DropChannel of Uuid: Uuid
    // user specific commands
    | Connect of nick: string * mat: MaterializeFlow option  * channels: Uuid list   // return UserInfo
    | Disconnect of user: Uuid
    | Join of user: Uuid * channelName: string * mat: MaterializeFlow option
    // | Nick of user: Uuid * newNick: string
    | Leave of user: Uuid * chanId: Uuid
    | GetUser of user: Uuid                             // returns UserInfo

type ServerReplyMessage =
    | ChannelList of ChannelInfo list
    | ChannelInfo of ChannelInfo option
    | UserInfo of UserInfo
    | Error of string

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

module internal Helpers =
    open ServerState

    /// Creates a user.
    let createUser nick : UserData =
        {id = Uuid.New(); nick = nick; email = None; channels = Map.empty; mat = None}

    let getUserNick userInfo = userInfo.nick
    let getChannelId (channel: ChannelData) = channel.id
    let getChanName (channel: ChannelData) = channel.name

    let updateIf p f o = if p o then f o else o

    let updateChannels f serverState: ServerData =
        {serverState with channels = serverState.channels |> List.map f}

    let updateUsers f serverState: ServerData =
        {serverState with users = serverState.users |> List.map f}

    let updateChannel f chanId serverState: ServerData =
        let u  chan = if chan.id = chanId then f chan else chan
        in
        updateChannels u serverState

    let byChanName name = getChanName >> ((=) name)
    let byChanId id = getChannelId >> ((=) id)

    let setChannelTopic topic (chan: ChannelData) =
        {chan with topic = topic}

    let updateUser f userId serverState: ServerData =
        let u (user: UserData) = if user.id = userId then f user else user
        in
        updateUsers u serverState

    let addUserChan chanId ks (user: UserData) =
        {user with channels = user.channels |> Map.add chanId ks}

    let leaveChan chanId (user: UserData) =
        {user with channels = user.channels |> Map.remove chanId}
    
    let getChannelInfo (data: ChannelData) =
        async {
            let! (users: Uuid list) = data.channelActor <? ListUsers
            return {id = data.id; name = data.name; topic = data.topic; userCount = users |> List.length}
        }
    let getChannelInfo0 (data: ChannelData) =
        {id = data.id; name = data.name; topic = data.topic; userCount = 0}

    let getUserInfo (data: UserData) (channels: ChannelData list) =
        let getChan ids =
            channels |> List.filter (fun chan -> ids |> Map.containsKey chan.id)
            |> List.map getChannelInfo0 // FIXME does not return userCount
        { id = data.id; nick = data.nick; email = data.email
          channels = data.channels |> getChan}

    module Async =
        let map f workflow = async {
            let! res = workflow
            return f res }

// type AddChanFnType = string -> Flow<Message, ChatClientMessage, Akka.NotUsed> -> UniqueKillSwitch

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
            chan |> getChannelInfo0, state
        | _ when isValidName name ->
            let channelActor = createChannel name
            let newChan = {
                id = Uuid.New()
                name = name; topic = ""
                channelActor = channelActor
                }
            newChan |> getChannelInfo0, {state with channels = newChan::state.channels}
        | _ ->
            failwith "Invalid channel name"

    let findChannel name (state: ServerData) =
        state.channels |> List.tryFind (byChanName name) |> Option.map getChannelInfo0

    let setTopic chanId newTopic state =
        state |> updateChannel (setChannelTopic newTopic) chanId

    let private kickUser chanId (u: UserData) =
        match u.channels |> Map.tryFind chanId with
        | Some (Some ks) ->
            do ks.Shutdown()
            {u with channels = u.channels |> Map.remove chanId}
        | Some _ ->
            {u with channels = u.channels |> Map.remove chanId}
        | _ -> u

    let dropChannel chanId state =
        match state.channels |> List.tryFind (byChanId chanId) with
        | Some chan ->
            let newState = state |> updateUsers (kickUser chanId)
            in
            {newState with channels = state.channels |> List.filter (not << byChanId chanId)}
        | _ -> state

open ServerState
open Helpers

/// Starts IRC server actor.
let startServer (system: ActorSystem) =

    let matchName name = getChanName >> ((=) name)
    let matchId id  = getChannelId >> ((=) id)

    let behavior state (ctx: Actor<ServerControlMessage>) =
        function
        | List ->
            ctx.Sender() <!| (state |> ServerApi.listChannels |> Async.map ServerReplyMessage.ChannelList)
            ignored state

        | NewChannel name ->
            let chanInfo, newState = state |> ServerApi.addChannel (createChannel system) name
            ctx.Sender() <! (Some chanInfo |> ServerReplyMessage.ChannelInfo)
            newState |> ignored

        | FindChannel name ->
            ctx.Sender() <!
                match state |> ServerApi.findChannel name with
                | None -> ServerReplyMessage.Error "Channel with such name not found"
                | chan -> chan |> ServerReplyMessage.ChannelInfo
            ignored state

        | SetTopic (chanId, topic) ->
            ignored (state |> ServerApi.setTopic chanId topic)

        | DropChannel chanId ->
            state |> ServerApi.dropChannel chanId |> ignored

        | Connect (nick, mat, channels) ->
            // checking nick is unique
            match state.users |> List.exists(fun u -> u.nick = nick) with
            | true ->
                ctx.Sender() <! ServerReplyMessage.Error "User with such nick already exists"
                ignored state
            | _ ->
                let newUser = createUser nick
                let newUser = {
                    newUser
                    with
                        mat = mat
                        channels = state.channels
                            |> List.filter(fun chan -> channels |> List.contains chan.id)
                            |> List.map (fun chan ->
                                let ks = mat |> Option.map (fun m -> m <| createPartyFlow chan.channelActor newUser.id)
                                chan.id, ks
                            )
                            |> Map.ofList
                }
                
                ctx.Sender() <! ServerReplyMessage.UserInfo (getUserInfo newUser state.channels)
                ignored {state with users = newUser :: state.users}
        
        | Disconnect userId ->
            match state.users |> List.tryFind (fun u -> u.id = userId) with
            | None ->
                ctx.Sender() <! ServerReplyMessage.Error "User with such id not found"
                ignored state
            | Some user ->
                user.channels |> Map.iter(fun _ ks ->
                    match ks with
                    | Some killSwitch -> killSwitch.Shutdown()
                    | _ -> ()
                )
                ignored {state with users = state.users |> List.filter(fun u -> u.id <> userId)}
                // closing socket will kick user off of all the channels

        | Join (userId, channelName, mat) ->
            let alreadyJoined channelName (u: UserData) =
                state.channels |> List.tryFind (matchName channelName)
                |> function
                | Some ch when u.channels |> Map.containsKey ch.id -> true
                | _ -> false

            match state.users |> List.tryFind (fun u -> u.id = userId) with
            | None ->
                ctx.Sender() <! ServerReplyMessage.Error "User with such id not found"
                ignored state
            | Some user when user |> alreadyJoined channelName ->
                ctx.Sender() <! ServerReplyMessage.Error "User already joined this channel"
                ignored state
            | Some user ->
                // TODO validate channel name
                let newState, chan =
                    match state.channels |> List.tryFind (matchName channelName) with
                    | None ->
                        let channelActor = createChannel system channelName
                        let newChan = {
                            id = Uuid.New()
                            name = channelName; topic = ""
                            channelActor = channelActor
                            }
                        {state with channels = newChan::state.channels}, newChan
                    | Some chan -> state, chan
                
                let ks = mat |> Option.map (fun m -> m <| createPartyFlow chan.channelActor userId)
                let newState = newState |> updateUser (addUserChan chan.id ks) userId
                ignored newState
        
        | Leave (userId, chanId) ->
            match state.users |> List.tryFind (fun u -> u.id = userId) with
            | None ->
                ctx.Sender() <! ServerReplyMessage.Error "User with such id not found"
                ignored state
            | Some user ->
                match user.channels |> Map.tryFind chanId with
                | None ->
                    ctx.Sender() <! ServerReplyMessage.Error "User is not joined channel"
                    ignored state
                | Some (Some ks) ->
                    do ks.Shutdown()
                    state |> updateUser (leaveChan chanId) userId |> ignored
                | _ ->
                    state |> updateUser (leaveChan chanId) userId |> ignored

        | GetUser userId ->
            match state.users |> List.tryFind (fun u -> u.id = userId) with
            | None ->
                ctx.Sender() <! ServerReplyMessage.Error "User with such id not found"
            | Some user ->
                ctx.Sender() <! ServerReplyMessage.UserInfo (getUserInfo user state.channels)

            ignored state

    in
    props <| actorOf2 (behavior { channels = []; users = [] }) |> (spawn system "ircserver")

(*
/// Creates an actor for echo bot.
let createEchoActor (system: ActorSystem) botUser =
    let botHandler state (ctx: Actor<_>) =
        function
        | ChatMessage (_, userId, Message message) // FIXME do not let bots reply to other bots when user.Person <> Person.Anonymous
            ->
            let reply = sprintf "\"%s\" said: %s" (user |> getUserNick) message
            do ctx.Sender() <! ChannelMessage.NewMessage (botUser, Message reply)
            ignored ()
        | _ -> ignored ()
    in
     props <| (actorOf2 <| botHandler ()) |> spawn system "echobot"

let createDiagChannel (system: ActorSystem) (server: IActorRef<_>) channelName =
    let botUser = createUser "echobot"
    let (User {id = userId}) = botUser
    let bot = createEchoActor system botUser
    async {
        let! (chan: obj) = server <? NewChannel channelName
        match chan with
        | :? option<ChannelInfo> as t when Option.isSome t ->
            let chan = Option.get t
            chan.channelActor <! (NewParticipant (userId, bot))
            ()
        | _ ->
            failwith "server replied with something other than new channel"

        return ()
    }
*)
// TODO incapsulate server actor (so that actor is not exposed as is and we provide nice api)?

let getChannelList (server: IActorRef<_>) =
    async {
        let! (ChannelList list) = server <? List
        return list
    }

let joinChannel (server: IActorRef<_>) chan user mat =
    server <! Join (user, chan, mat)
