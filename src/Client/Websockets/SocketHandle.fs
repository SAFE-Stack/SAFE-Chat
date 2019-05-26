namespace Fable.Websockets.Elmish

open System
open Fable.Websockets.Protocol
open Fable.Websockets.Client
open Fable.Import

type SocketHandle<'serverMsg> = private {
    ConnectionId: Guid
    CloseHandle: ClosedCode -> string -> unit
    // Source: IObservable<WebsocketEvent<'clientMsg>>
    Sink: 'serverMsg -> unit
    mutable Subscription: IDisposable option
}

type Msg<'serverMsg, 'clientMsg, 'applicationMsg> =
        | WebsocketMsg of SocketHandle<'serverMsg> * WebsocketEvent<'clientMsg>
        | ApplicationMsg of 'applicationMsg

//  with
//     override x.GetHashCode() = x.ConnectionId.GetHashCode()
//  member x.Equals(b) =
//     match b with
//     | :? SocketHandle<_,_> as c -> x.ConnectionId = c.ConnectionId
//     | _ -> false

[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SocketHandle =        
    let Blackhole () : SocketHandle<'serverMsg> =
        {   Sink = ignore
            // Source = Fable.Websockets.Observables.Subject()
            CloseHandle = fun _ _ -> ()
            ConnectionId = Guid.Empty
            Subscription = None }

    let inline Create address (dispatcher: Elmish.Dispatch<Msg<'serverMsg,'clientMsg,'applicationMsg>>) =
        let (sink,source, closeHandle) = establishWebsocketConnection<'serverMsg,'clientMsg> address                    
        let connection =
            {   Sink = sink
                // Source = source
                CloseHandle = closeHandle
                ConnectionId = Guid.NewGuid()
                Subscription = None }
        
        let subscription = source |> Observable.subscribe (fun msg ->
            Browser.Dom.console.log ("msg", msg)
            Msg.WebsocketMsg (connection, msg) |> dispatcher)

        Browser.Dom.console.log "Create done"
        connection.Subscription <- Some subscription
