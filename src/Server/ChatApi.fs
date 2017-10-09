module ChatApi
// implements chat API endpoints for Suave

open Akka.Actor
open Akkling
open Akkling.Streams
open Akka.Streams.Dsl

open Suave
open Suave.Successful
open Suave.WebSocket

open ChatServer
open SocketFlow

// open ProductHub.Rest.Common
open SessionStore
open SessionFlow

module private Payloads =
    type ChanUserInfo = {
        name: string; online: bool; isbot: bool; lastSeen: System.DateTime
    }
    type ChannelInfo = {
        id: int; name: string; userCount: int; topic: string; joined: bool; users: ChanUserInfo list
    }

    type Message = {
        id: int; ts: System.DateTime; text: string; chan: string; author: string
    }

open Payloads

let private encodeChannelMessage channel : ChatClientMessage -> WsMessage =
    function
    | ChatMessage ((id, ts), author, Message message) ->
        {id = id; ts = ts; text = message; chan = channel; author = author}
    | Joined ((id, ts), user, _) ->
        {id = id; ts = ts; text = sprintf "%s joined channel" user; chan = channel; author = "system"}
    | Left ((id, ts), user, _) ->
        {id = id; ts = ts; text = sprintf "%s left channel" user; chan = channel; author = "system"}
    >> Json.json >> WsMessage.Text

/// <summary>
/// Translates user flow to a WebSocket ready one.
/// </summary>
let private mapUserToWebsocketFlow channel (userFlow: Flow<Message, ChatClientMessage, _>) =

    Flow.empty<WsMessage, Akka.NotUsed>
    |> Flow.map (function | Text t -> t |_ -> null)
    |> Flow.filter ((<>) null)
    |> Flow.map Message
    |> Flow.via userFlow
    |> Flow.map (encodeChannelMessage channel)

let private ensureSessionCreated sessionStore sessionId =
    async {
        let! sessionState =
            sessionStore |> SessionStore.readData<SessionState> sessionId "sessionCtl"
        return
            match sessionState with
            | Some state -> state
            | _ ->
                let sessionControl = SessionControl()
                sessionStore |> SessionStore.writeData sessionId "sessionCtl" sessionControl
                sessionControl
    }

type private ServerActor = IActorRef<IrcServerControlMsg>

let inSession f: WebPart =
    f "111" // FIXME retrieve session from http context

/// Lists a channels.
let listChannels (server: ServerActor) (sessionStore: SessionStore) : WebPart =
    inSession <| fun session ctx ->
        async {
            let! userChatSession = ensureSessionCreated sessionStore session.SessionId
            let! channelList = getChannelList server

            // FIXME overall uglyness
            let myChannels = userChatSession |> listChannels
            let makePayload name =
                let imIn = myChannels |> List.contains name
                {Payloads.ChannelInfo.name = name; topic = ""; id = 1; userCount = 3; users = []; joined = imIn}
            let result = channelList |> List.map (makePayload) |> Json.json
            return! OK result ctx
        }

let chanInfo (server: ServerActor) (chanName: string) : WebPart =
    inSession <| fun session ctx ->
        async {
            let! (channel: IrcChannel option) = server <? (GetChannel chanName)
            let! users =
                match channel with
                | Some {channelActor = channelActor} ->
                    async {
                        let! (users: string list) = channelActor <? ListUsers
                        return users
                    }
                | _ -> async.Return []

            let name = if Option.isSome channel then chanName else ""
            let userInfo user =
                {Payloads.ChanUserInfo.name = user; online = true; isbot = false; lastSeen = System.DateTime.Now}

            let chanInfo = {
                Payloads.ChannelInfo.name = name
                topic = "" // TODO
                joined = false // TODO
                id = 1; userCount = List.length users 
                users = users |> List.map userInfo
            }

            return! OK (chanInfo |> Json.json) ctx
        }

let join (server: ServerActor) (sessionStore: SessionStore) chan : WebPart =
    inSession (fun session ctx ->
        async {
            let! chatSession = ensureSessionCreated sessionStore session
            do! joinChannel server chan
            return! OK "joined" ctx
        }    
    )

let leave (server: ServerActor) (sessionStore: SessionStore) chan : WebPart =
    inSession (fun session ctx ->
        async {
            let! sessionControl = ensureSessionCreated sessionStore session.SessionId
            do sessionControl.LeaveChannel chan
            return! OK "left" ctx
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
