module RestApi
// implements chat API endpoints for Suave

open Akka.Actor
open Akkling
open Akkling.Streams
open Akka.Streams.Dsl

open Suave
open Suave.Filters
open Suave.Logging
open Suave.Successful
open Suave.RequestErrors
open Suave.WebSocket
open Suave.Operators

open ChannelFlow
open ChatServer
open SocketFlow

open FsChat

type private ServerActor = IActorRef<ChatServer.ServerControlMessage>
type Session = NoSession | UserLoggedOn of UserNick * ActorSystem * ServerActor

type IncomingMessage =
    | ChannelMessage of Uuid * Message
    | ControlMessage of Protocol.ServerMsg
    | Trash

module private Implementation =

    let encodeChannelMessage channel : UserNick ChatClientMessage -> Protocol.ClientMsg =
        // FIXME fill user info
        let userInfo (UserNick nickname) : Protocol.ChanUserInfo =
            {nick = nickname; name = "TBD"; email = None; online = true; isbot = false; lastSeen = System.DateTime.Now}

        function
        | ChatMessage ((id, ts),  UserNick author, Message message) ->
            Protocol.ChanMsg {id = id; ts = ts; text = message; chan = channel; author = author}
        | Joined ((id, ts), user, _) ->
            Protocol.UserJoined  ({id = id; ts = ts; user = userInfo user}, channel)
        | Left ((id, ts), user, _) ->
            Protocol.UserLeft  ({id = id; ts = ts; user = userInfo user}, channel)

    let mapChannel isMine (chan: ChatServer.ChannelInfo) : Protocol.ChannelInfo =
        {id = chan.id.ToString(); name = chan.name; topic = chan.topic; userCount = chan.userCount; users = []; joined = isMine chan.id}

    let logger = Log.create "chatapi"        

    type ServerActor = IActorRef<ServerControlMessage>

    let getChannelList (server: ServerActor) me =
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
                logger.error (Message.eventX "Failed to get channel list. Reason: {reason}" >> Message.setFieldValue "reason" e)
                return! BAD_REQUEST e ctx

            | Ok channelList ->
                let! (UserInfo u) = server <? GetUser me
                let (UserNick nickname) = u.nick
                let reply = Protocol.Hello {
                    nick = nickname; name = u.name; email = u.email
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
                let getNickname (UserNick nick) = nick
                let userInfo userNick :Protocol.ChanUserInfo list =
                    serverState.users |> List.tryFind (fun u -> u.nick = userNick)
                    |> Option.map<_, Protocol.ChanUserInfo>
                        (fun user -> {nick = getNickname user.nick; name = user.name; email = user.email; online = true; isbot = false; lastSeen = System.DateTime.Now}) // FIXME
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

    let sendError errorText = Protocol.CannotProcess ("", errorText) |> Protocol.ClientMsg.Error

    let join (server: ServerActor) me chanIdStr =
        async {
            match Uuid.TryParse chanIdStr with
            | Some chanId ->
                let! x = server <? Join (me, chanId)
                match x with
                | ChannelInfo info ->
                    return info |> mapChannel (fun _ -> true) |> Protocol.JoinedChannel
                | Error e ->
                    return sendError e
            | None -> return sendError "invalid channel id"
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

    let makeHelloMsg (server: ServerActor) me =
        async {
            // TODO run on server side using ReadState
            let! reply = getChannelList server me

            match reply with
            | Result.Error e ->
                logger.error (Message.eventX "Failed to get channel list. Reason: {reason}"
                    >> Message.setFieldValue "reason" e
                )
                let errText = sprintf "Failed to get channel list. Reason: '%s'" e
                return errText |> Protocol.AuthFail |> Protocol.Error

            | Ok channelList ->
                let! (UserInfo u) = server <? GetUser me
                return Protocol.Hello <| {nick = getNickname u.nick; name = u.name; email = u.email; channels = channelList}
        }

    // extracts message from websocket reply, only handles User input (channel * string)
    let extractMessage message =
        try
            match message with
            | Text t ->
                match t |> Json.unjson<Protocol.ServerMsg> with
                | Protocol.UserMessage msg ->
                    match Uuid.TryParse msg.chan with
                    | Some chanId -> ChannelMessage (chanId, Message msg.text)
                    | _ -> Trash
                | message -> ControlMessage message                
            |_ -> Trash
        with e ->
            do logger.error (Message.eventX "Failed to parse message '{msg}': {e}" >> Message.setFieldValue "msg" message  >> Message.setFieldValue "e" e)
            Trash
    let isChannelMessage = function
        | ChannelMessage (_,_) -> true | _ -> false
    let extractChannelMessage (ChannelMessage (chan, message)) = chan, message

    let isControlMessage = function
        | ControlMessage _ -> true | _ -> false
    let extractControlMessage (ControlMessage message) = message

    let connectWebSocket (system: ActorSystem) (server: ServerActor) me : WebPart =

        let processControlMessage : Flow<Protocol.ServerMsg, Protocol.ClientMsg, _> =
            let processMsg (message: Protocol.ServerMsg) : Protocol.ClientMsg list Async =
                async {
                    match message with
                    | Protocol.Join chanId ->
                        let! result = join server me chanId
                        return [result]
                    | m ->
                        do logger.error (Message.eventX "Unhandled message {msg}" >> Message.setFieldValue "msg" m)
                        // return []
                        return [Protocol.CannotProcess ("", sprintf "not implemented: %A" m) |> Protocol.ClientMsg.Error]
                }
            Flow.empty<_, Akka.NotUsed> |> Flow.asyncMap 10 processMsg |> Flow.collect id

        fun ctx -> async {
            let materializer = system.Materializer()

            // let server know websocket has gone (see onCompleteMessage)
            // FIXME not sure if it works at all
            let monitor t = async {
                logger.debug (Message.eventX "Monitor triggered for user {me}" >> Message.setFieldValue "me" me)
                let! _ = t
                let! reply = server <? Disconnect (me)
                return ()
            }

            let sessionFlow = createUserSessionFlow<UserNick,Uuid> materializer

            let userMessageFlow =
                Flow.empty<IncomingMessage, Akka.NotUsed>
                |> Flow.filter isChannelMessage
                |> Flow.map extractChannelMessage
                |> Flow.log "User flow"
                |> Flow.viaMat sessionFlow Keep.right
                |> Flow.map (fun (channel: Uuid, message) -> encodeChannelMessage (channel.ToString()) message)

            let controlFlow =   // FIXME could be simplified
                Flow.empty<IncomingMessage, Akka.NotUsed>
                |> Flow.filter isControlMessage
                |> Flow.map extractControlMessage
                |> Flow.log "Control flow"
                |> Flow.via processControlMessage

            let combineFlow = FlowImpl.split2 userMessageFlow controlFlow Keep.left

            let socketFlow =
                Flow.empty<WsMessage, Akka.NotUsed>
                |> Flow.watchTermination (fun x t -> (monitor t) |> Async.Start; x)
                |> Flow.map extractMessage
                |> Flow.log "Extracting message"
                |> Flow.viaMat combineFlow Keep.right
                |> Flow.map (Json.json >> Text)

            let! helloMessage = makeHelloMsg server me  // FIXME retrieve data from server

            let materialize materializer (source: Source<WsMessage, Akka.NotUsed>) (sink: Sink<WsMessage, _>) =
                let listenChannel =
                    source
                    |> Source.viaMat socketFlow Keep.right
                    |> Source.mergeMat (Source.Single (helloMessage |> Json.json |> Text)) Keep.left
                    |> Source.toMat sink Keep.left
                    |> Graph.run materializer

                server <? Connect (me, listenChannel) |> Async.RunSynchronously |> ignore

            let handler socket =
                handleWebsocketMessages system materialize socket
            return! handShake handler ctx
        }    

open Implementation

let jsonNoCache =
    Writers.setHeader "Cache-Control" "no-cache, no-store, must-revalidate"
    >=> Writers.setHeader "Pragma" "no-cache"
    >=> Writers.setHeader "Expires" "0"
    >=> Writers.setMimeType "application/json"

let api (session: Session): WebPart =
    pathStarts "/api" >=> jsonNoCache >=>
        match session with
        | UserLoggedOn (nick, actorSys, server) ->
            choose [
                POST >=> path "/api/hello" >=> (hello server nick)
                GET  >=> path "/api/channels" >=> (listChannels server nick)
                GET  >=> pathScan "/api/channel/%s/info" (chanInfo server nick)
                POST >=> pathScan "/api/channel/%s/joincreate" (joinOrCreate server nick)
                POST >=> pathScan "/api/channel/%s/leave" (leave server nick)
                path "/api/socket" >=> (connectWebSocket actorSys server nick)
            ]
        | NoSession ->
            BAD_REQUEST "Authorization required"