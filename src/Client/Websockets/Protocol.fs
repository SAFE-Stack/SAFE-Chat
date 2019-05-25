namespace Fable.Websockets

module Protocol =

    open System

    type ClosedCode = int
    type ClosedEvent = { code: ClosedCode; reason:string; wasClean: bool }
    type WebsocketEvent<'applicationProtocol> = 
        | Msg of 'applicationProtocol
        | Closed of ClosedEvent
        | Opened
        | Error
        | Exception of Exception

    type CloseHandle = ClosedCode -> string -> unit

    type SendMessage<'protocol> = 'protocol -> unit    