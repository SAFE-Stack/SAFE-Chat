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
open System

type UserSessionData = {
    Nickname: string
    Id: string
    UserId: Uuid
}

type private ServerActor = IActorRef<ChatServer.ServerControlMessage>
type Session = NoSession | UserLoggedOn of UserSessionData * ActorSystem * ServerActor

module private Implementation =

    let encodeChannelMessage channel : Uuid ChatClientMessage -> WsMessage =
        // FIXME fill user info
        let userInfo userid : Protocol.ChanUserInfo =
            {id = userid.ToString(); nick = userid.ToString(); online = true; isbot = false; lastSeen = System.DateTime.Now}

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
                logger.error (Message.eventX "Failed to get channel list. Reason: {reason}"
                    >> Message.setFieldValue "reason" e
                )
                return! BAD_REQUEST e ctx

            | Ok channelList ->
                let! (UserInfo u) = server <? GetUser me
                let reply = Protocol.Hello {
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
                        (fun user -> {id = user.id.ToString(); nick = user.nick; online = true; isbot = false; lastSeen = System.DateTime.Now}) // FIXME
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
    let extractMessage = function
        | Text t ->
            match t |> Json.unjson<Protocol.ServerMsg> with
            | Protocol.UserMessage msg ->
                match Uuid.TryParse msg.chan with
                | Some chanId ->
                    Some (chanId, Message msg.text)
                | _ -> None
        |_ -> None

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
         
                return Protocol.Hello <| {
                    nickname = u.nick; userId = (u.id.ToString())
                    channels = channelList
                    }
        }

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

            let! helloMessage = makeHelloMsg server me  // FIXME retrieve data from server

            let materialize materializer (source: Source<WsMessage, Akka.NotUsed>) (sink: Sink<WsMessage, _>) =
                let listenChannel =
                    source
                    |> Source.viaMat socketFlow Keep.right
                    |> Source.mergeMat (Source.Single (helloMessage |> Json.json |> Text)) Keep.left
                    // |> Source.alsoToMat logSink Keep.left
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
        | UserLoggedOn (u, actorSys, server) ->
            choose [
                POST >=> path "/api/hello" >=> (hello server u.UserId)
                GET  >=> path "/api/channels" >=> (listChannels server u.UserId)
                GET  >=> pathScan "/api/channel/%s/info" (chanInfo server u.UserId)
                POST >=> pathScan "/api/channel/%s/join" (join server u.UserId)
                POST >=> pathScan "/api/channel/%s/joincreate" (joinOrCreate server u.UserId)
                POST >=> pathScan "/api/channel/%s/leave" (leave server u.UserId)
                path "/api/socket" >=> (connectWebSocket actorSys server u.UserId)
            ]
        | NoSession ->
            BAD_REQUEST "Authorization required"