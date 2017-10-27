module ChatApi
// implements chat API endpoints for Suave

open Akka.Actor
open Akkling
open Akkling.Streams
open Akka.Streams.Dsl

open Suave
open Suave.Successful
open Suave.RequestErrors
open Suave.WebSocket

open ChannelFlow
open ChatServer
open SocketFlow

module private Payloads =
    type ChanUserInfo = {
        name: string; online: bool; isbot: bool; lastSeen: System.DateTime
    }
    type ChannelInfo = {
        id: string; name: string; userCount: int; topic: string; joined: bool; users: ChanUserInfo list
    }

    type Message = {
        id: int; ts: System.DateTime; text: string; chan: string; author: string
    }

module private Implementation =

    open Payloads

    let mapMessageUser<'Ti, 'To> f : 'Ti ChatClientMessage -> 'To ChatClientMessage =
        function
        | ChatMessage ((id, ts), author, message) -> ChatMessage ((id, ts), f author, message)
        | Joined ((id, ts), user, users) -> Joined ((id, ts), f user, users |> Seq.map f)
        | Left ((id, ts), user, users) -> Left ((id, ts), f user, users |> Seq.map f)

    let encodeChannelMessage channel : Uuid ChatClientMessage -> WsMessage =
        mapMessageUser (fun userid -> userid.ToString())
        >>
        function
        | ChatMessage ((id, ts), author, Message message) ->
            {Message.id = id; ts = ts; text = message; chan = channel; author = author}
        | Joined ((id, ts), user, _) ->
            {id = id; ts = ts; text = sprintf "%s joined channel" user; chan = channel; author = "system"}
        | Left ((id, ts), user, _) ->
            {id = id; ts = ts; text = sprintf "%s left channel" user; chan = channel; author = "system"}
        >> Json.json >> Text

    let mapChannel isMine (chan: ChatServer.ChannelInfo) =
        {Payloads.ChannelInfo.id = chan.id.ToString(); name = chan.name; topic = chan.topic; userCount = chan.userCount; users = []; joined = isMine chan.id}

open Implementation

type private ServerActor = IActorRef<ServerControlMessage>

/// Lists a channels.
let listChannels (server: ServerActor) me : WebPart =
    fun ctx -> async {
        let! reply = server <? List
        match reply with
        | Error e ->
            return! BAD_REQUEST e ctx
        | ChannelList channelList ->
            let! reply2 = server <? GetUser me
            match reply2 with
            | Error e ->
                return! BAD_REQUEST e ctx
            | UserInfo me ->
                let imIn chanId = me.channels |> List.exists(fun ch -> ch.id = chanId)
                let result = channelList |> List.map (mapChannel imIn) |> Json.json
                return! OK result ctx
            | _ -> return! BAD_REQUEST "Unknown reply from server, expected user info" ctx
        | _ -> return! BAD_REQUEST "Unknown reply from server" ctx
    }

open Payloads

let chanInfo (server: ServerActor) me (chanName: string) : WebPart =
    fun ctx -> async {
        let! (serverState: ServerState.ServerData) = server <? ReadState
        let! chan = serverState |> ServerApi.findChannelEx chanName
        match chan with
        | Result.Error e ->
            return! BAD_REQUEST e ctx
        | Ok channel ->
            let userInfo userId :Payloads.ChanUserInfo list =
                serverState.users |> List.tryFind (fun u -> u.id = userId)
                |> function
                | Some user -> [{name = user.nick; online = true; isbot = false; lastSeen = System.DateTime.Now}]  // FIXME
                | _ -> []

            let chanInfo: Payloads.ChannelInfo = {
                id = channel.id.ToString()
                name = channel.name
                topic = channel.topic
                joined = channel.users |> List.contains me
                userCount = channel.userCount
                users = channel.users |> List.collect userInfo
            }

            return! OK (chanInfo |> Json.json) ctx
    }

let join (server: ServerActor) me chan : WebPart =
    fun ctx -> async {
        let! x = server <? Join (me, chan)
        match x with
        | Error e ->
            return! BAD_REQUEST e ctx
        | _ ->
            return! OK "" ctx
    }    

let leave (server: ServerActor) me chanIdStr : WebPart =
    fun ctx -> async {
        let (Some chanId) = Uuid.TryParse(chanIdStr)    // FIXME, let it understand both id and name
        let! x = server <? Leave (me, chanId)
        match x with
        | Error e ->
            return! BAD_REQUEST e ctx
        | _ ->
            return! OK "" ctx
    }    

// TODO need a socket protocol type

// extracts message from websocket reply, only handles User input (channel * string)
let private extractMessage = function
    | Text t ->
        let payload = t |> Json.unjson<Payloads.Message>
        match Uuid.TryParse payload.chan with
        | Some chanId ->
            Some (chanId, Message payload.text)
        | _ -> None
    |_ -> None

let connectWebSocket (system: ActorSystem) (server: ServerActor) me : WebPart =
    fun ctx -> async {
        let materializer = system.Materializer()

        // let server know websocket has gone (see onCompleteMessage)
        // FIXME not sure if it works at all
        let monitor t = async {
            let! _ = t
            let! reply = server <? Disconnect (me)
            return ()
        }

        let sessionFlow = createUserSessionFlow<Uuid,Uuid> materializer
        let socketFlow =
            Flow.empty<WsMessage, Akka.NotUsed>
            |> Flow.watchTermination (fun x t -> (monitor t) |> Async.Start; x)
            |> Flow.map extractMessage
            |> Flow.filter Option.isSome
            |> Flow.map Option.get
            |> Flow.viaMat sessionFlow Keep.right
            |> Flow.map (fun (channel: Uuid, message) -> encodeChannelMessage (channel.ToString()) message)

        let materialize materializer (source: Source<WsMessage, Akka.NotUsed>) (sink: Sink<WsMessage, Akka.NotUsed>) =
            let listenChannel =
                source
                |> Source.viaMat socketFlow Keep.right
                |> Source.toMat sink Keep.left
                |> Graph.run materializer

            server <? Connect (me, listenChannel) |> Async.RunSynchronously |> ignore

        return! handShake (handleWebsocketMessages system materialize) ctx
    }    
