namespace Fable.Websockets.Elmish

open System
open Fable.Websockets.Protocol
open Fable.Websockets.Client

type SocketHandle<'serverMsg> = private {
    ConnectionId: string
    CloseHandle: ClosedCode -> string -> unit
    Sink: 'serverMsg -> unit
    mutable Subscription: IDisposable option
} with
    override x.GetHashCode() = x.ConnectionId.GetHashCode()
    override x.Equals(b: obj) =
        match b with
        | :? SocketHandle<'serverMsg> as c -> x.ConnectionId = c.ConnectionId
        | _ -> false

type Msg<'serverMsg, 'clientMsg, 'applicationMsg> =
        | WebsocketMsg of SocketHandle<'serverMsg> * WebsocketEvent<'clientMsg>
        | ApplicationMsg of 'applicationMsg

[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SocketHandle =        
    let Blackhole () : SocketHandle<'serverMsg> =
        {   Sink = ignore
            CloseHandle = fun _ _ -> ()
            ConnectionId = "empty"
            Subscription = None }

    let inline Create address (dispatcher: Elmish.Dispatch<Msg<'serverMsg,'clientMsg,'applicationMsg>>) =
        let (sink,source, closeHandle) = establishWebsocketConnection<'serverMsg,'clientMsg> address                    
        let connection =
            {   Sink = sink
                CloseHandle = closeHandle
                ConnectionId = Guid.NewGuid().ToString()
                Subscription = None }
        
        let subscription = source |> Observable.subscribe (fun msg ->
            Msg.WebsocketMsg (connection, msg) |> dispatcher)

        connection.Subscription <- Some subscription
