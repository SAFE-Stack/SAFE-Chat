module RestApi
// implements chat API endpoints for Suave

open System
open Akka.Actor
open Akkling
open Akkling.Streams
open Akka.Streams
open Akka.Streams.Dsl

open Suave
open Suave.Logging
open Suave.WebSocket

open ChannelFlow
open ChatServer
open SocketFlow

open FsChat

let private logger = Log.create "chatapi"        

module private Implementation =
    let inline (|OtherwiseFail|) _ = failwith "no choice"

    type ServerActor = IActorRef<ChatServer.ServerControlMessage>

    type IncomingMessage =
        | ChannelMessage of int * Message
        | ControlMessage of Protocol.ServerMsg
        | Trash of reason: string

    let userInfo user : Protocol.ChanUserInfo = {nick = user.nick; isbot = user.isbot}

    let encodeChannelMessage channel : Party ChatClientMessage -> Protocol.ClientMsg =
        function
        | ChatMessage ((id, ts),  author: Party, Message message) ->
            Protocol.ChanMsg {id = id; ts = ts; text = message; chan = channel; author = author.nick}
        | Joined ((id, ts), user, _) ->
            Protocol.UserJoined  ({id = id; ts = ts; user = userInfo user}, channel)
        | Left ((id, ts), user, _) ->
            Protocol.UserLeft  ({id = id; ts = ts; user = userInfo user}, channel)

    let (|ParseChannelId|_|) s = 
        let (result, value) = Int32.TryParse s
        if result then Some value else None

    // extracts message from websocket reply, only handles User input (channel * string)
    let extractMessage message =
        try
            match message with
            | Text t ->
                match t |> Json.unjson<Protocol.ServerMsg> with
                | Protocol.UserMessage msg ->
                    match msg.chan with
                    | ParseChannelId chanId -> ChannelMessage (chanId, Message msg.text)
                    | _ -> Trash "Bad channel id"
                | message -> ControlMessage message                
            | x -> Trash <| sprintf "Not a Text message '%A'" x
        with e ->
            do logger.error (Message.eventX "Failed to parse message '{msg}': {e}" >> Message.setFieldValue "msg" message  >> Message.setFieldValue "e" e)
            Trash "exception"
    let isChannelMessage = function |ChannelMessage _ -> true | _ -> false
    let isControlMessage = function |ControlMessage _ -> true | _ -> false

    let extractChannelMessage (ChannelMessage (chan, message) | OtherwiseFail (chan, message)) = chan, message
    let extractControlMessage (ControlMessage message | OtherwiseFail message) = message

    let mapChanInfo (data: ChannelData) : Protocol.ChannelInfo =
        {id = data.id.ToString(); name = data.name; topic = data.topic; userCount = 0; users = []; joined = false}

    let setJoined v (ch: Protocol.ChannelInfo) =
        {ch with joined = v}

    let replyErrorProtocol requestId errtext =
        Protocol.CannotProcess (requestId, errtext) |> Protocol.ClientMsg.Error

    let reply requestId = function
        | Ok response ->    response
        | Result.Error e -> replyErrorProtocol requestId e

open Implementation
open UserSession

let connectWebSocket ({server = server; me = me; actorSystem = actorSystem }) : WebPart =

    let materializer = actorSystem.Materializer()

    // session data
    let mutable session = UserSession.make server me
    let mutable listenChannel = None

    let updateSession requestId f = function
        | Ok (newSession, response) -> session <- newSession; f response
        | Result.Error e ->            replyErrorProtocol requestId e

    let makeChannelInfoResult v =
        async {
            match v with
            | Ok (arg1, channel: ChannelData) ->
                let! (users: Party list) = channel.channelActor <? ListUsers
                let chaninfo = { mapChanInfo channel with users = users |> List.map userInfo}
                return Ok (arg1, chaninfo)
            | Result.Error e -> return Result.Error e
        }

    let processControlMessage message =
        async {
            let requestId = "" // TODO take from server message
            match message with

            | Protocol.ServerMsg.Greets ->
                let! serverChannels = server |> (listChannels (fun _ -> true))

                let makeChanInfo chanData =
                    { mapChanInfo chanData with joined = session.channels |> Map.containsKey chanData.id}

                let makeHello channels =
                    Protocol.ClientMsg.Hello {nick = me.nick; name = ""; email = None; channels = channels}

                return serverChannels |> Result.map (List.map makeChanInfo >> makeHello) |> reply ""

            | Protocol.ServerMsg.Join chanIdStr ->
                match chanIdStr with
                | ParseChannelId channelId ->
                    let! result = session |> UserSession.join listenChannel channelId
                    let! chaninfo = makeChannelInfoResult result
                    return chaninfo |> updateSession requestId (setJoined true >> Protocol.JoinedChannel)
                | _ -> return replyErrorProtocol requestId "bad channel id"

            | Protocol.ServerMsg.JoinOrCreate channelName ->
                let! channelResult = server |> getOrCreateChannel channelName
                match channelResult with
                | Ok channelData ->
                    let! result = session |> UserSession.join listenChannel channelData.id
                    let! chaninfo = makeChannelInfoResult result
                    return chaninfo |> updateSession requestId (setJoined true >> Protocol.JoinedChannel)
                | Result.Error err ->
                    return replyErrorProtocol requestId err

            | Protocol.ServerMsg.Leave chanIdStr ->
                return chanIdStr |> function
                    | ParseChannelId channelId ->
                        let result = session |> UserSession.leave channelId
                        result |> updateSession requestId (fun _ -> Protocol.LeftChannel chanIdStr)
                    | _ ->
                        replyErrorProtocol requestId "bad channel id"

            | _ ->
                return replyErrorProtocol requestId "event was not processed"
        }

    let sessionFlow = createUserSessionFlow<Party,int> materializer
    let controlMessageFlow = Flow.empty<_, Akka.NotUsed> |> Flow.asyncMap 10 processControlMessage

    let serverEventsSource: Source<Protocol.ClientMsg, Akka.NotUsed> =
        let party = { Party.Make me.nick with isbot = me.isbot }
        let notifyNew sub = subscribeNotify server party sub; Akka.NotUsed.Instance
        let source = Source.actorRef OverflowStrategy.Fail 1 |> Source.mapMaterializedValue notifyNew

        source |> Source.map (function
            | AddChannel ch -> ch |> (mapChanInfo >> Protocol.ClientMsg.NewChannel)
            | DropChannel ch -> ch |> (mapChanInfo >> Protocol.ClientMsg.RemoveChannel)
            | m ->
                logger.error (Message.eventX "Unknown notification message {m}" >> Message.setFieldValue "m" m)
                Protocol.ClientMsg.Error (Protocol.CannotProcess ("", "Unknown notification message"))
        )

    let userMessageFlow =
        Flow.empty<IncomingMessage, Akka.NotUsed>
        |> Flow.filter isChannelMessage
        |> Flow.map extractChannelMessage
        |> Flow.log "User flow"
        |> Flow.viaMat sessionFlow Keep.right
        |> Flow.map (fun (channel: int, message) -> encodeChannelMessage (channel.ToString()) message)

    let controlFlow =
        Flow.empty<IncomingMessage, Akka.NotUsed>
        |> Flow.filter isControlMessage
        |> Flow.map extractControlMessage
        |> Flow.log "Control flow"
        |> Flow.via controlMessageFlow
        |> Flow.mergeMat serverEventsSource Keep.left

    let combinedFlow : Flow<IncomingMessage,Protocol.ClientMsg,_> =
        FlowImpl.split2 userMessageFlow controlFlow Keep.left

    let socketFlow =
        Flow.empty<WsMessage, Akka.NotUsed>
        |> Flow.map extractMessage
        |> Flow.log "Extracting message"
        |> Flow.viaMat combinedFlow Keep.right
        |> Flow.map (Json.json >> Text)

    let materialize materializer (source: Source<WsMessage, Akka.NotUsed>) (sink: Sink<WsMessage, _>) =
        listenChannel <-
            source
            |> Source.viaMat socketFlow Keep.right
            |> Source.toMat sink Keep.left
            |> Graph.run materializer |> Some
        ()

    handShake (handleWebsocketMessages actorSystem materialize)
