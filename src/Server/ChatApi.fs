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

// open ProductHub.Rest.Common
open SessionStore

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
        >> Json.json >> WsMessage.Text

    let mapChannel isMine (chan: ChatServer.ChannelInfo) =
        {Payloads.ChannelInfo.id = chan.id.ToString(); name = chan.name; topic = chan.topic; userCount = chan.userCount; users = []; joined = isMine chan.id}

open Implementation

/// <summary>
/// Translates user flow to a WebSocket ready one.
/// </summary>
let private mapUserToWebsocketFlow channel (userFlow: Flow<ChannelFlow.Message, Uuid ChatClientMessage, _>) =

    Flow.empty<WsMessage, Akka.NotUsed>
    |> Flow.map (function | Text t -> t |_ -> null)
    |> Flow.filter ((<>) null)
    |> Flow.map Message
    |> Flow.via userFlow
    |> Flow.map (encodeChannelMessage channel)

type private ServerActor = IActorRef<ServerControlMessage>

let inSession f: WebPart =
    f "111" // FIXME retrieve session from http context

/// Lists a channels.
let listChannels (server: ServerActor) (sessionStore: SessionStore) : WebPart =
    inSession <| fun session ctx ->
        // FIXME overall uglyness
        // using exceptions with try catch might do a trick

        let meId = Uuid.New() // TODO obtain id from session
        async {
            let! reply = server <? List
            match reply with
            | ServerReplyMessage.Error e ->
                return! BAD_REQUEST e ctx
            | ServerReplyMessage.ChannelList channelList ->
                let! reply2 = server <? GetUser meId
                match reply2 with
                | ServerReplyMessage.Error e ->
                    return! BAD_REQUEST e ctx
                | ServerReplyMessage.UserInfo me ->
                    let imIn chanId = me.channels |> List.exists(fun ch -> ch.id = chanId)
                    let result = channelList |> List.map (mapChannel imIn) |> Json.json
                    return! OK result ctx
                | _ -> return! BAD_REQUEST "Unknown reply from server, expected user info" ctx
            | _ -> return! BAD_REQUEST "Unknown reply from server" ctx
        }

open Payloads

let chanInfo (server: ServerActor) (chanName: string) : WebPart =
    inSession <| fun session ctx ->
        let meId = Uuid.New()   // TODO retrieve from session
        async {
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
                    joined = channel.users |> List.contains meId
                    userCount = channel.userCount
                    users = channel.users |> List.collect userInfo
                }

                return! OK (chanInfo |> Json.json) ctx
        }

let join (server: ServerActor) (sessionStore: SessionStore) chan : WebPart =
    inSession (fun session ctx ->
        let meId = Uuid.New()   // TODO retrieve from session
        async {
            let! x = server <? Join (meId, chan)
            match x with
            | Error e ->
                return! BAD_REQUEST e ctx
            | _ ->
                return! OK "" ctx
        }    
    )

let leave (server: ServerActor) (sessionStore: SessionStore) chan : WebPart =
    inSession (fun session ctx ->
        let meId = Uuid.New()   // TODO retrieve from session
        async {
            let! x = server <? Leave (meId, chan)
            match x with
            | Error e ->
                return! BAD_REQUEST e ctx
            | _ ->
                return! OK "" ctx
        }    
    )

let startChat (system: ActorSystem) (server: ServerActor) (users: User Repository) (sessionStore) : WebPart =
    let materializer = system.Materializer()

    let extractMessage =
        function
        | Text t ->
            let p = Providers.UserInput.Parse t in
            p.Chan, Message p.Message
        |_ -> null, Message null

    let toWebSocketFlow sessionFlow =
        Flow.empty<WsMessage, Akka.NotUsed>
        |> Flow.map extractMessage
        |> Flow.filter (fst >> (<>) null)
        |> Flow.viaMat sessionFlow Keep.right
        |> Flow.map (fun (channel, message) -> encodeChannelMessage channel message)

    let sessionFlow = startUserSession materializer
    let flow = toWebSocketFlow sessionFlow

    let materialize user (sessionControl: SessionState) materializer (source: Source<WsMessage, Akka.NotUsed>) (sink: Sink<WsMessage, Akka.NotUsed>) =
        let listenChannel =
            source
            |> Source.viaMat flow Keep.right
            |> Source.toMat sink Keep.Left
            |> Graph.run materializer
        let joinChan chan =
            async {
                let! userFlow = joinChannel server chan user
                let leaveChan = listenChannel chan userFlow
                return leaveChan
            }
        // FIXME pass channels
        connect [] joinChan |> Async.RunSynchronously

    inSession (fun session ctx ->
        async {
            let! userEnvp = session.UserId |> users.GetById
            let user = (Option.get userEnvp).Record
            let! sessionControl = ensureSessionCreated sessionStore session.SessionId

            return! handShake (handleWebsocketMessagesImpl system (materialize user sessionControl)) ctx
        }    
    )
