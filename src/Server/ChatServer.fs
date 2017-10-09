module ChatServer

open System

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Streams

open ChannelFlow

type MaterializeFlow = Flow<Message,Uuid ChatClientMessage, Akka.NotUsed> -> UniqueKillSwitch

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
    | Join of user: Uuid * channelName: string
    // | Nick of user: Uuid * newNick: string
    | Leave of user: Uuid * channelName: string
    | GetUser of user: Uuid                             // returns UserInfo

type ChannelInfo = {id: Uuid; name: string; topic: string; userCount: int}
type UserInfo = {id: Uuid; nick: string; email: string option; channels: ChannelInfo list}

type ServerReplyMessage =
    | ChannelList of ChannelInfo list
    | ChannelInfo of ChannelInfo
    | UserInfo of UserInfo
    | Error of string


module internal Internals =

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

    /// Creates a user.
    let createUser nick : UserData =
        {id = Uuid.New(); nick = nick; email = None; channels = Map.empty; mat = None}

    let getUserNick userInfo = userInfo.nick
    let getChannelId (channel: ChannelData) = channel.id
    let getChanName (channel: ChannelData) = channel.name

    let updateIf p f o = if p o then f o else o

    let updateChannels f serverState: ServerData =
        {serverState with channels = serverState.channels |> List.map f}

    let updateChannel f chanId serverState: ServerData =
        let u  chan = if chan.id = chanId then f chan else chan
        in
        updateChannels u serverState
    
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
// type AddChanFnType = string -> Flow<Message, ChatClientMessage, Akka.NotUsed> -> UniqueKillSwitch

open Internals

/// Starts IRC server actor.
let startServer (system: ActorSystem) =

    let matchName name = getChanName >> ((=) name)
    let matchId id  = getChannelId >> ((=) id)

    let behavior state (ctx: Actor<ServerControlMessage>) =
        function
        | List ->
            ctx.Sender() <!| async {
                let! channels = state.channels |> List.map getChannelInfo |> Async.Parallel
                return channels |> Array.toList |> ServerReplyMessage.ChannelList
            }
            ignored state

        | NewChannel name ->
            let channelActor = createChannel system name
            let newChan = {
                id = Uuid.New()
                name = name; topic = ""
                channelActor = channelActor
                }
            ctx.Sender() <! (newChan |> getChannelInfo0 |> ServerReplyMessage.ChannelInfo)
            {state with channels = newChan::state.channels} |> ignored

        | FindChannel name ->
            ctx.Sender() <!| async {
                match state.channels |> List.tryFind (matchName name) with
                | None ->
                    return ServerReplyMessage.Error "Channel with such name not found"
                | Some chan ->
                    let! chanInfo = chan |> getChannelInfo
                    return chanInfo |> ServerReplyMessage.ChannelInfo
            }
            ignored state

        | SetTopic (chanId, topic) ->
            let updateTopic = updateIf (matchId chanId) (fun chan -> {chan with topic = topic})
            ignored (state |> updateChannels updateTopic)

        | DropChannel chanId ->
            match state.channels |> List.tryFind (matchId chanId) with
            | Some chan ->
                // kicks users off the channel
                let newUserList = state.users |> List.map (fun (u: UserData) ->
                        match u.channels |> Map.tryFind chanId with
                        | Some (Some ks) ->
                            do ks.Shutdown()
                            {u with channels = u.channels |> Map.remove chanId}
                        | Some _ ->
                            {u with channels = u.channels |> Map.remove chanId}
                        | _ -> u
                    )
                let newChanList = state.channels |> List.filter (not << matchId chanId)
                in
                {state with users = newUserList; channels = newChanList}
            | _ -> state
            |> ignored

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
                                let flow = createPartyFlow chan.channelActor newUser.id
                                let ks = mat |> Option.map (fun m -> m flow)
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

let joinChannel (server: IActorRef<_>) chan user =
    server <! Join (user, chan)
