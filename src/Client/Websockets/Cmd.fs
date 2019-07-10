namespace Fable.Websockets.Elmish

module Cmd =
    let public ofSocketMessage (socket: SocketHandle<'serverMsg>) (message:'serverMsg) : Elmish.Cmd<Msg<'serverMsg,'clientMsg,'applicationMsg>> =
        [fun (dispatcher : Elmish.Dispatch<Msg<'serverMsg,'clientMsg,'applicationMsg>>) -> socket.Sink message]

    let inline public tryOpenSocket address : Elmish.Cmd<Msg<'serverMsg,'clientMsg,'applicationMsg>> =
        [fun (dispatcher : Elmish.Dispatch<Msg<'serverMsg,'clientMsg,'applicationMsg>>) -> SocketHandle.Create address dispatcher]

    let public closeSocket (socket: SocketHandle<'serverMsg>) code reason : Elmish.Cmd<Msg<'serverMsg,'clientMsg,'applicationMsg>> =
        [fun (dispatcher : Elmish.Dispatch<Msg<'serverMsg,'clientMsg,'applicationMsg>>) -> do socket.CloseHandle code reason] 