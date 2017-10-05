module ChatServer

open System

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Streams

open Types
open Channel

module internal Internals =

    /// Channel is a primary store for channel info and data
    type ChannelData = {
        id: Uuid
        info: ChannelInfo
        newUser: User -> Flow<Message, ChatClientMessage, Akka.NotUsed>
        channelActor: IActorRef<ChannelMessage>
    }

    type ServerData = {
        channels: ChannelData list
    }

    // todo move to own modules

    /// Creates a user impersonated by bots. User seems too heavy for our needs.
    let createUser nick =
        User {UserInfo.Blank with id = Uuid.New(); nick = nick}

    let getUserNick (User info) = info.nick

type ServerControlMessage =
    | List
    | NewChannel of name: string
    | SetTopic of chan: string * topic: string
    // RenameChan
    | DropChannel of name: string
    | GetChannel of name: string   // this needs to be refined

type ServerReplyMessage =
    | ChannelList of string list

// type AddChanFnType = string -> Flow<Message, ChatClientMessage, Akka.NotUsed> -> UniqueKillSwitch

open Internals

/// Starts IRC server actor.
let startServer (system: ActorSystem) =

    let getChanName ch = ch.info.name
    let matchName name = getChanName >> ((=) name)

    let behavior state (ctx: Actor<ServerControlMessage>) =
        function
        | List ->
            do ctx.Sender() <! (state.channels |> List.map getChanName |> ChannelList)
            ignored state

        | NewChannel name ->
            let channelActor = createChannel system name
            let newChan = {
                id = Uuid.New()
                info = {name = name; topic = ""}
                newUser = createPartyFlow channelActor
                channelActor = channelActor
                }
            ctx.Sender() <! (Some newChan)
            {state with channels = newChan::state.channels} |> ignored

        | GetChannel name ->
            let chan = state.channels |> List.tryFind (matchName name)
            ctx.Sender() <! chan
            ignored state

        | SetTopic (name, topic) ->
            let updateTopic chan =
                if matchName name chan then
                    {chan with info = {chan.info with topic = topic}}
                else chan
            ignored {state with channels = state.channels |> List.map updateTopic}

        | DropChannel name ->
            match state.channels |> List.tryFind (matchName name) with
            | Some chan ->
                // TODO notify users, kick them off the channel
                let newChanList = state.channels |> List.filter (not << matchName name)
                in
                {state with channels = newChanList} |> ignored
            | _ -> ignored state
    in
    props <| actorOf2 (behavior { channels = [] }) |> (spawn system "ircserver")

/// Creates an actor for echo bot.
let createEchoActor (system: ActorSystem) botUser =
    let botHandler state (ctx: Actor<_>) =
        function
        | ChatMessage (_, user, Message message) // FIXME do not let bots reply to other bots when user.Person <> Person.Anonymous
            ->
            let reply = sprintf "\"%s\" said: %s" (user |> getUserNick) message
            do ctx.Sender() <! ChannelMessage.NewMessage (botUser, Message reply)
            ignored ()
        | _ -> ignored ()
    in
     props <| (actorOf2 <| botHandler ()) |> spawn system "echobot"

let createDiagChannel (system: ActorSystem) (server: IActorRef<_>) channelName =
    let botUser = createUser "echobot"
    let bot = createEchoActor system botUser
    async {
        let! (chan: obj) = server <? NewChannel channelName
        match chan with
        | :? option<ChannelData> as t when Option.isSome t ->
            let chan = Option.get t
            chan.channelActor <! (NewParticipant (botUser, bot))
            ()
        | _ ->
            failwith "server replied with something other than new channel"

        return ()
    }

// TODO incapsulate server actor (so that actor is not exposed as is and we provide nice api)?

let getChannelList (server: IActorRef<_>) : string list Async =
    async {
        let! (ChannelList list) = server <? List
        return list
    }

let joinChannel (server: IActorRef<_>) (chanName: string) (user: User) =
    async {
        let! (response: ChannelData option) = server <? GetChannel chanName
        match response with
        | Some channel ->
            return channel.newUser user
        | _ ->        
            let! (response: ChannelData option) = server <? NewChannel chanName
            let channel = Option.get response
            return channel.newUser user
    }
