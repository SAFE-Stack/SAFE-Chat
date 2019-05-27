module Fable.Websockets.Client

open System
open Fable.Import

open Fable.Websockets.Observables
open Fable.Websockets.Protocol

open Browser
open Browser.Types

open Thoth.Json

let inline private toJson x = Encode.Auto.toString(0, x)
let inline private ofJson<'T> json = Decode.Auto.unsafeFromString<'T>(json)

// open Fable.Core.JS
// let inline private toJson x = JSON.stringify x
// let inline private ofJson<'T> json = JSON.parse json :?> 'T

let toObj () = obj()

let inline private receiveMessage<'clientProtocol> (receiveSubject:Subject<WebsocketEvent<'clientProtocol>>) (msgEvent:MessageEvent) =         
    try
        Dom.console.log("receivemsg1", msgEvent.data)
        let msg = ofJson<'clientProtocol> (string msgEvent.data)
        Dom.console.log("receivemsg2", msg)
        Msg msg
    with     
        | e -> Exception e
    |> receiveSubject.Next
    |> toObj

let inline private receiveCloseEvent<'clientProtocol, 'serverProtocol> (receiveSubject:Subject<WebsocketEvent<'clientProtocol>>) (sendSubject:Subject<'serverProtocol>) (closeEvent:CloseEvent) =             
    let closedCode = closeEvent.code
    let payload  = { code = closedCode; reason= closeEvent.reason; wasClean=closeEvent.wasClean }    
        
    do payload 
    |> WebsocketEvent.Closed 
    |> receiveSubject.Next      

    do sendSubject.Completed()
    do receiveSubject.Completed()
    
    obj()

let inline private sendMessage (websocket: WebSocket) (receiveSubject:Subject<WebsocketEvent<'a>>) msg =    
    try 
        let jsonMsg = msg |> toJson
        do websocket.send jsonMsg    
    with 
        | e -> receiveSubject.Next (Exception e)


let inline public establishWebsocketConnection<'serverProtocol, 'clientProtocol> (uri:string) : 
    (('serverProtocol->unit)*IObservable<WebsocketEvent<'clientProtocol>>*(ClosedCode->string->unit)) = 


    let receiveSubject = Subject<WebsocketEvent<'clientProtocol>>() 
    let sendSubject = Subject<'serverProtocol>()
    
    let websocket = WebSocket.Create(uri)
    
    let connection = (sendSubject.Subscribe (sendMessage websocket receiveSubject))

    let closeHandle (code:ClosedCode) (reason:string) = 
        let state = websocket.readyState
        if state = WebSocketState.CONNECTING || state = WebSocketState.OPEN then
            websocket.close(code, reason)          
            connection.Dispose() 
        else ()    

    websocket.onmessage <- fun msg -> (receiveMessage<'clientProtocol> receiveSubject msg)
    websocket.onclose <- fun msg -> (receiveCloseEvent receiveSubject sendSubject msg)
    websocket.onopen <- fun _ -> receiveSubject.Next Opened |> toObj                                 
    websocket.onerror <- fun _ -> receiveSubject.Next Error |> toObj                                     
    
    (sendSubject.Next, receiveSubject :> IObservable<_>, closeHandle)