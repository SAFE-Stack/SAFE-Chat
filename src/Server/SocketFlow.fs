module SocketFlow

open System
open System.Text

open Akka.Actor
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Streams

open Suave.Logging
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

type WsMessage =
    | Text of string
    | Data of byte array
    | Close
    | Ignore

let private logger = Log.create "socketflow"

// Provides websocket handshaking. Connects web socket to a pair of Source and Sync.
// 'materialize'
let handleWebsocketMessages (system: ActorSystem)
    (materialize: IMaterializer -> Source<WsMessage, Akka.NotUsed> -> Sink<WsMessage, _> -> unit) (ws : WebSocket)
    =
    let materializer = system.Materializer()
    let sourceActor, inputSource =
        Source.actorRef OverflowStrategy.Fail 1000 |> Source.toMat Sink.publisher Keep.both
        |> Graph.run materializer |> fun (actor, pub) -> actor, Source.FromPublisher pub

    let emptyData = ByteSegment [||]

    let asyncIgnore f = async {
        let! _ = f
        return WsMessage.Ignore :> obj
    }

    // sink for flow that sends messages to websocket
    let sinkBehavior _ (ctx: Actor<obj>): obj -> _ =
        function
        | :? WsMessage as wsmsg ->
            wsmsg |> function
            | Terminated _ ->
                ws.send Opcode.Close emptyData true |> Async.Ignore |> Async.Start
                stop()
            | Text text ->
                // using pipeTo operator just to wait for async send operation to complete
                ws.send Opcode.Text (Encoding.UTF8.GetBytes(text) |> ByteSegment) true
                    |> asyncIgnore |!> ctx.Self
                ignored()
            | Data bytes ->
                ws.send Binary (ByteSegment bytes) true |> asyncIgnore |!> ctx.Self
                ignored()
            | Ignore ->
                ignored()
            | Close ->
                stop()
        | _ ->
            ignored ()

    let sinkActor =
        props <| actorOf2 (sinkBehavior ()) |> (spawn system null) |> retype

    let sink: Sink<WsMessage,_> = Sink.ActorRef(untyped sinkActor, Text "asdfsdf") // TODO PoisonPill.Instance
    do materialize materializer inputSource sink

    logger.debug (Message.eventX "materialized socket sink")

    fun _ -> 
        socket {
            do! ws.send Opcode.Text (Encoding.UTF8.GetBytes("test test test") |> ByteSegment) true
            
            let loop = ref true
            while !loop do
                let! msg = ws.read()
                
                match msg with
                | (Opcode.Text, data, true) -> 
                    let str = Encoding.UTF8.GetString data
                    sourceActor <! Text str
                | (Ping, _, _) ->
                    do! ws.send Pong emptyData true
                | (Opcode.Close, _, _) ->
                    // this finalizes the Source
                    sourceActor <! Close
                    do! ws.send Opcode.Close emptyData true
                    loop := false
                | _ -> ()
        }

/// Creates Suave socket handshaking handler
let handleWebsocketMessagesFlow  (system: ActorSystem) (handler: Flow<WsMessage, WsMessage, Akka.NotUsed>) (ws : WebSocket) =
    let materialize materializer inputSource sink =
        inputSource |> Source.via handler |> Source.runWith materializer sink |> ignore
    handleWebsocketMessages system materialize ws
