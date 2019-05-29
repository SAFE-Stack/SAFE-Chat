module Chat.State

open Browser.Dom

open Elmish
open Elmish.Navigation

open Fable.Websockets.Elmish
open Fable.Websockets.Protocol

open Router

open Channel.Types
open Chat.Types

open FsChat

module private Conversions =

    let mapUserInfo isMe (u: Protocol.ChanUserInfo) :UserInfo =
        { Id = u.id; Nick = u.nick; IsBot = u.isbot
          Status = u.status
          Online = true; ImageUrl = Core.Option.ofObj u.imageUrl
          isMe = isMe u.id}

    // TODO duplicate
    let mapChannel (ch: Protocol.ChannelInfo) : Channel.Types.ChannelInfo =
        {Id = ch.id; Name = ch.name; Topic = ch.topic; UserCount = ch.userCount}

    let mapUserMessage (msg: Protocol.ChannelMessageInfo) =
        (msg.author, {Id = msg.id; Ts = msg.ts; Content = msg.text})

module private Implementation =

    let getUser (userId: string) (users: Map<UserId,UserInfo>) : UserInfo =
        users |> Map.tryFind userId |> Option.defaultWith (fun () -> {
            Id = userId; Nick = "Unknown #" + userId; Status = ""
            IsBot = false; Online = true; ImageUrl = None; isMe = false})

    let socketMsgUpdate msg =
        let joinChannel channel (chat: RemoteServer.Types.Model) =
            // TODO this should put channel to a Connecting state
            let chanData, cmd =
                Channel.State.init() |> fst
                |> Channel.State.update (Init (channel, [], []))
            {chat with Channels = chat.Channels |> Map.add channel.Id chanData}, cmd

        function
        | Connected chat as state ->
            let { user = me; serverData = serverData; socket = socket } = chat
            let isMe = (=) me.Id
            match msg with

            | Protocol.Hello hello ->
                let me = Conversions.mapUserInfo ((=) hello.me.id) hello.me
                let channels = hello.channels |> List.map (fun ch -> ch.id, Conversions.mapChannel ch) |> Map.ofList
                in
                let remoteServerData, _ = RemoteServer.State.init() // TODO pass second parameter
                Connected { user = me; socket = socket; serverData = { remoteServerData with ChannelList = channels } }, Cmd.none

            | Protocol.CmdResponse (reqId, reply) ->
                match reply with
                | Protocol.UserUpdated newUser ->
                    let meNew = Conversions.mapUserInfo isMe newUser
                    in
                    Connected { chat with user = meNew }, Cmd.none

                | Protocol.CommandResponse.JoinedChannel chanInfo ->
                    let newServerData, cmd = serverData |> joinChannel (Conversions.mapChannel chanInfo)
                    in
                    Connected { chat with serverData = newServerData }, Cmd.batch [
                          cmd |> Cmd.map (fun msg -> RemoteServer.Types.ChannelMsg (chanInfo.id, msg) |> ApplicationMsg)
                          Channel chanInfo.id |> toHash |> Navigation.newUrl ]

                | Protocol.LeftChannel channelId ->
                    serverData.Channels |> Map.tryFind channelId
                    |> function
                    | Some _ ->
                        let newServerState = { serverData with Channels = serverData.Channels |> Map.remove channelId}
                        Connected { chat with serverData = newServerState }, Overview |> toHash |> Navigation.newUrl
                    | _ ->
                        console.error ("Channel not found", channelId)
                        state, Cmd.none

                | Protocol.Pong ->
                    
                    console.debug ("Pong", reqId)
                    state, Cmd.none

                | Protocol.Error error ->
                    console.error (sprintf "Server replied with error %A" error)    // FIXME report error to user
                    state, Cmd.none

            | protocolMsg ->
                let newServerData, cmd = RemoteServer.State.chatUpdate isMe protocolMsg serverData
                Connected { chat with serverData = newServerData }, cmd |> Cmd.map ApplicationMsg
        | other ->
            console.info (sprintf "Socket message %A" other)
            (other, Cmd.none)

open Implementation

let init () : ChatState * Cmd<Msg> =
    let socketAddr = sprintf "ws://%s/api/socket" document.location.host
    console.debug ("Opening socket", socketAddr)
    NotConnected, Cmd.tryOpenSocket socketAddr

let update msg state : ChatState * Cmd<Msg> = 
    match msg with
    | ApplicationMsg amsg ->
        match state with
        | Connected chat ->
            let newServerModel, cmd, serverMsg = RemoteServer.State.update amsg chat.serverData
            let commands =
                [ Cmd.map ApplicationMsg cmd ]
                @ (serverMsg |> Option.map (Cmd.ofSocketMessage chat.socket) |> Option.toList)

            Connected { chat with serverData = newServerModel }, Cmd.batch commands
        | _ ->
            console.error "Failed to process channel message. Server is not connected"
            state, Cmd.none

    | WebsocketMsg (socket, Opened) ->
        let remoteServerState, _ =  RemoteServer.State.init()   // TODO pass second parameter
        Connected { user = UserInfo.Anon; serverData = remoteServerState; socket = socket }, Cmd.ofSocketMessage socket Protocol.ServerMsg.Greets

    | WebsocketMsg (_, Msg socketMsg) ->
        socketMsgUpdate socketMsg state

    | _ -> state, Cmd.none
