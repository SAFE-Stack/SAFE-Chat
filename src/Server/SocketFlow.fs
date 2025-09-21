module SocketFlow

open System
open System.Text

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Streams

open Microsoft.Extensions.Logging
open System.Net.WebSockets
open System.Threading

type WsMessage =
    | Text of string
    | Data of byte array
    | Ignore

let private logger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore).CreateLogger("socketflow")

// Provides websocket handshaking. Connects web socket to a pair of Source and Sync.
// 'materialize'
let handleWebsocketMessages (system: ActorSystem)
    (materialize: IMaterializer -> Source<WsMessage, Akka.NotUsed> -> Sink<WsMessage, _> -> unit) (ws : WebSocket) (ct: CancellationToken)
    =
    let materializer = system.Materializer()
    let sourceActor, inputSource =
        Source.actorRef OverflowStrategy.Fail 1000 |> Source.toMat Sink.publisher Keep.both
        |> Graph.run materializer |> fun (actor, pub) -> actor, Source.FromPublisher pub

    let buffer = Array.zeroCreate 4096

    // sink for flow that sends messages to websocket
    let sinkBehavior (ctx: Actor<WsMessage>) : WsMessage -> Effect<_> =
        function
        | Text text ->
            let bytes = Encoding.UTF8.GetBytes(text)
            let segment = ArraySegment<byte>(bytes)
            async {
                do! ws.SendAsync(segment, WebSocketMessageType.Text, true, ct) |> Async.AwaitTask
            } |> Async.Catch |> Async.Ignore |> Async.Start
            // TODO process ws exceptions
            ignored ()
        | Data bytes ->
            let segment = ArraySegment<byte>(bytes)
            async {
                do! ws.SendAsync(segment, WebSocketMessageType.Binary, true, ct) |> Async.AwaitTask
            } |> Async.Catch |> Async.Ignore |> Async.Start
            // TODO process ws exceptions
            ignored ()
        | Ignore -> ignored ()

    let sinkActor =
        props <| actorOf2 sinkBehavior |> (spawn system null) |> retype

    let sink: Sink<WsMessage,_> = Sink.ActorRef(untyped sinkActor, PoisonPill.Instance, fun _ -> PoisonPill.Instance)
    do materialize materializer inputSource sink

    let rec receiveLoop () = async {
        if not ct.IsCancellationRequested && ws.State = WebSocketState.Open then
            let! result = ws.ReceiveAsync(ArraySegment<byte>(buffer), ct) |> Async.AwaitTask
            
            match result.MessageType with
            | WebSocketMessageType.Text -> 
                let str = Encoding.UTF8.GetString(buffer, 0, result.Count)
                sourceActor <! Text str
                return! receiveLoop()
            | WebSocketMessageType.Binary ->
                let bytes = Array.sub buffer 0 result.Count
                sourceActor <! Data bytes
                return! receiveLoop()
            | WebSocketMessageType.Close ->
                logger.LogDebug("Received WebSocket Close, terminating actor")
                retype sourceActor <! PoisonPill.Instance
                do! ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct) |> Async.AwaitTask
            | _ -> return! receiveLoop()
    }
    
    receiveLoop()

/// Creates ASP.NET Core WebSocket handshaking handler  
let handleWebsocketMessagesFlow  (system: ActorSystem) (handler: Flow<WsMessage, WsMessage, Akka.NotUsed>) (ws : WebSocket) (ct: CancellationToken) =
    let materialize materializer inputSource sink =
        inputSource |> Source.via handler |> Source.runWith materializer sink |> ignore
    handleWebsocketMessages system materialize ws ct
