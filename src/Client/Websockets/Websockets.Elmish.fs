module Websockets.Elmish

open Browser.WebSocket
open Browser.Types
open Elmish

type SocketHandle<'serverMsg> = {
    send: ('serverMsg -> unit)
    close: (unit -> unit)
}

type WebsocketEvent<'serverMsg, 'clientMsg> =
    | Opened of SocketHandle<'serverMsg>
    | Closed
    | Error of string
    | Message of 'clientMsg

let connectWebSocket<'serverMsg, 'clientMsg> 
    (address: string)
    (serialize: 'serverMsg -> string)
    (deserialize: string -> 'clientMsg option)
    (dispatch: WebsocketEvent<'serverMsg, 'clientMsg> Dispatch)
    : ('serverMsg -> unit) * (unit -> unit) =
    
    let mutable ws: WebSocket option = None
    
    let send data =
        match ws with
        | Some socket when socket.readyState = WebSocketState.OPEN
            -> socket.send(serialize data)
        | _ -> ()
    
    let close() =
        match ws with
        | Some socket when
            socket.readyState = WebSocketState.OPEN || socket.readyState = WebSocketState.CONNECTING
            -> socket.close()
        | _ -> ()
    
    let subscribe (dispatch: WebsocketEvent<'serverMsg, 'clientMsg> Dispatch) =
        let socket = WebSocket.Create address
        ws <- Some socket

        socket.onopen <- fun _ -> dispatch (Opened { send = send; close = close })
        socket.onclose <- fun _ -> dispatch Closed
        socket.onerror <- fun _ -> dispatch (Error "WebSocket error")
        socket.onmessage <- fun event ->
            match deserialize (event.data :?> string) with
            | Some msg -> dispatch (Message msg)
            | None -> dispatch (Error "Failed to deserialize message")
        
        send, close

    subscribe dispatch
