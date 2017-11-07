module ChatApi
// implements chat API endpoints for Suave

open Akka.Actor
open Akkling
open Akkling.Streams
open Akka.Streams.Dsl

open Suave
open Suave.Logging
open Suave.Successful
open Suave.RequestErrors
open Suave.WebSocket

open ChannelFlow
open ChatServer
open SocketFlow

open FsChat

module private Implementation =

    let encodeChannelMessage channel : Uuid ChatClientMessage -> WsMessage =
        // FIXME fill user info
        let userInfo userid : Protocol.ChanUserInfo = {name = userid.ToString(); online = true; isbot = false; lastSeen = System.DateTime.Now}

        function
        | ChatMessage ((id, ts), author, Message message) ->
            Protocol.ChanMsg {id = id; ts = ts; text = message; chan = channel; author = author.ToString()}
        | Joined ((id, ts), user, _) ->
            Protocol.UserJoined  ({id = id; ts = ts; user = userInfo user}, channel)
        | Left ((id, ts), user, _) ->
            Protocol.UserLeft  ({id = id; ts = ts; user = userInfo user}, channel)
        >> Json.json >> Text

    let mapChannel isMine (chan: ChatServer.ChannelInfo) : Protocol.ChannelInfo =
        {id = chan.id.ToString(); name = chan.name; topic = chan.topic; userCount = chan.userCount; users = []; joined = isMine chan.id}

    let logger = Log.create "chatapi"        

open Implementation

type private ServerActor = IActorRef<ServerControlMessage>

let private getChannelList (server: ServerActor) me =
    async {
        let! reply = server <? List
        match reply with
        | Error e ->
            return Result.Error (sprintf "Failed to get channel list: %s" e)
        | ChannelList channelList ->
            let! reply2 = server <? GetUser me
            match reply2 with
            | Error e ->
                return Result.Error (sprintf "Failed to get user by id '%s': %s" (me.ToString()) e)
            | UserInfo me ->
                let imIn chanId = me.channels |> List.exists(fun ch -> ch.id = chanId)
                let result = channelList |> List.map (mapChannel imIn)
                return Ok result
            | _ -> return Result.Error "Unknown reply from server, expected user info"
        | _ -> return Result.Error "Unknown reply from server"
    }

let hello (server: ServerActor) me : WebPart =
    fun ctx -> async {
        let! reply = getChannelList server me

        match reply with
        | Result.Error e ->
            logger.error (Message.eventX "Failed to get channel list. Reason: {reason}"
                >> Message.setFieldValue "reason" e
            )
            return! BAD_REQUEST e ctx

        | Ok channelList ->
            let! (UserInfo u) = server <? GetUser me
            let reply: Protocol.Hello = {
                nickname = u.nick; userId = (u.id.ToString())
                channels = channelList
                }

            return! OK (Json.json reply) ctx
    }

/// Lists all channels.
let listChannels (server: ServerActor) me : WebPart =
    fun ctx -> async {
        let! reply = getChannelList server me
        match reply with
        | Result.Error e ->
            return! BAD_REQUEST e ctx
        | Ok channelList ->
            return! OK (Json.json channelList) ctx
    }

/// Gets channel info by channel name
let chanInfo (server: ServerActor) me (chanName: string) : WebPart =
    fun ctx -> async {
        let! (serverState: ServerState.ServerData) = server <? ReadState
        let! chan = serverState |> ServerApi.findChannelEx chanName
        match chan with
        | Result.Error e ->
            return! BAD_REQUEST e ctx
        | Ok channel ->
            let userInfo userId :Protocol.ChanUserInfo list =
                serverState.users |> List.tryFind (fun u -> u.id = userId)
                |> Option.map<_, Protocol.ChanUserInfo>
                    (fun user -> {name = user.nick; online = true; isbot = false; lastSeen = System.DateTime.Now}) // FIXME
                |> Option.toList

            let chanInfo: Protocol.ChannelInfo = {
                id = channel.id.ToString()
                name = channel.name
                topic = channel.topic
                joined = channel.users |> List.contains me
                userCount = channel.userCount
                users = channel.users |> List.collect userInfo
            }

            return! OK (chanInfo |> Json.json) ctx
    }

let joinOrCreate (server: ServerActor) me channelName : WebPart =
    fun ctx -> async {
        let! x = server <? JoinOrCreate (me, channelName)
        match x with
        | Error e ->
            return! BAD_REQUEST e ctx
        | ChannelInfo info ->
            let response = info |> mapChannel (fun _ -> true)
            return! OK (Json.json response) ctx
    }    

let join (server: ServerActor) me chanIdStr : WebPart =
    Uuid.TryParse chanIdStr |> function
    | None -> BAD_REQUEST "invalid chanid"
    | Some chanId ->
        fun ctx -> async {
            let! x = server <? Join (me, chanId)
            match x with
            | Error e ->
                return! BAD_REQUEST e ctx
            | ChannelInfo info ->
                let response = info |> mapChannel (fun _ -> true)
                return! OK (Json.json response) ctx
        }    

let leave (server: ServerActor) me chanIdStr : WebPart =
    match Uuid.TryParse chanIdStr with
    | None -> BAD_REQUEST "channel not found"
    | Some chanId ->
        fun ctx -> async {
            let! result = server <? Leave (me, chanId)
            match result with
            | Error e -> return! BAD_REQUEST e ctx
            | _ ->       return! OK "" ctx
        }    

// TODO need a socket protocol type

// extracts message from websocket reply, only handles User input (channel * string)
let private extractMessage = function
    | Text t ->
        let payload = t |> Json.unjson<Protocol.ClientSocketMsg>
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
