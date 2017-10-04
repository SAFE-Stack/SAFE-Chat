module fschat.Flowr.ChatServer

open System

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Streams

open Types
open Channel

type IrcChannel = {
    name: string
    newUser: User -> Flow<Message, ChatProtocolMessage, Akka.NotUsed>
    channelActor: IActorRef<ChannelCtlMsg>
}

type IrcServerState = {
    channels: IrcChannel list
}

type IrcServerControlMsg =
    | List
    | NewChannel of name: string
    | DropChannel of name: string
    | GetChannel of name: string   // this needs to be refined

type IrcServerReplyMsg =
    | ChannelList of string list

// type AddChanFnType = string -> Flow<Message, ChatProtocolMessage, Akka.NotUsed> -> UniqueKillSwitch

/// <summary>
/// Starts IRC server actor.
/// </summary>
/// <param name="system"></param>
let startServer (system: ActorSystem) =

    let behavior state (ctx: Actor<IrcServerControlMsg>) =
        function
        | List ->
            do ctx.Sender() <! (state.channels |> List.map (fun chan -> chan.name) |> ChannelList)
            ignored state

        | NewChannel name ->
            let channelActor = createChannel system name
            let newUser = createPartyFlow channelActor
            let newChan = {name = name; newUser = newUser; channelActor = channelActor}
            ctx.Sender() <! (Some newChan)
            {state with channels = newChan::state.channels} |> ignored

        | GetChannel name ->
            let chan = state.channels |> List.tryFind (fun ch -> ch.name = name)
            ctx.Sender() <! chan
            ignored state

        | DropChannel name ->
            match state.channels |> List.tryFind (fun ch -> ch.name = name) with
            | Some chan ->
                // TODO notify users
                let newChanList = state.channels |> List.filter (fun ch -> ch.name <> name)
                in
                {state with channels = newChanList} |> ignored
            | _ -> ignored state
    in
    props <| actorOf2 (behavior { channels = [] }) |> (spawn system "ircserver")

/// Creates a user impersonated by bots. User seems too heavy for our needs.
let createUser name =
    // { UserApi.blank with LoginName = name}
    name

/// Creates an actor for echo bot.
let createEchoActor (system: ActorSystem) botUser =
    let botHandler state (ctx: Actor<_>) =
        function
        | ChatMessage (_, user, Message message) // FIXME do not let bots reply to other bots when user.Person <> Person.Anonymous
            ->
            do ctx.Sender() <! ChannelCtlMsg.NewMessage (botUser, sprintf "\"%s\" said: %s" user message |> Message)
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
        | :? option<IrcChannel> as t when Option.isSome t ->
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
        let! (response: IrcChannel option) = server <? GetChannel chanName
        let! channel =
            async {
                match response with
                | Some chanOpt ->
                    return chanOpt
                | None ->
                    let! (response: IrcChannel option) = server <? NewChannel chanName
                    return Option.get response
            }
        return channel.newUser user
    }

type UserMessage = UserMessage of chan: string * Message

/// Starts a user session. Return stopSession killswitch,
/// and "subscribe" method which adds chat flow (you have to create it in advance) to session flow.
/// "Subscribe" in its turn returns "unsubscribe" killswitch.
let startUserSession (materializer: Akka.Streams.IMaterializer) =

    let inhub = BroadcastHub.Sink<UserMessage>(bufferSize = 256)
    let outhub = MergeHub.Source<string * ChatProtocolMessage>(perProducerBufferSize = 16)

    let sourceTo (sink) (source: Source<'TOut, 'TMat>) = source.To(sink)

    let combine
            (producer: Source<UserMessage, Akka.NotUsed>)
            (consumer: Sink<string*ChatProtocolMessage, Akka.NotUsed>)
            (chanName: string) (chanFlow: Flow<Message, ChatProtocolMessage, _>) =

        let infilter =
            Flow.empty<UserMessage, Akka.NotUsed>
            |> Flow.filter (fun (UserMessage (chan,_)) -> chan = chanName)
            |> Flow.map (fun (UserMessage (_,message)) -> message)

        let graph =
            producer
            |> Source.viaMat (KillSwitches.Single()) Keep.right
            |> Source.via infilter
            |> Source.via chanFlow
            |> Source.map (fun message -> chanName, message)
            |> sourceTo consumer

        graph |> Graph.run materializer

    Flow.ofSinkAndSourceMat inhub combine outhub
