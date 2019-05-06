module Chat.State

open Elmish
open Elmish.Browser.Navigation

open Fable.Import.Browser
open Fable.Websockets.Elmish
open Fable.Websockets.Protocol
open Fable.Websockets.Elmish.Types

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

    let mapChannel (ch: Protocol.ChannelInfo) : ChannelInfo =
        {Id = ch.id; Name = ch.name; Topic = ch.topic; UserCount = ch.userCount}

    let mapUserMessage (msg: Protocol.ChannelMessageInfo) = (msg.author, {Id = msg.id; Ts = msg.ts; Content = msg.text})

module private Implementation =

    let updateChanCmd chanId (f: ChannelData -> ChannelData * Channel.Types.Msg Cmd) (chat: ChatData) : ChatData * Chat.Types.AppMsg Cmd=
        match chat.Channels |> Map.tryFind chanId with
        | Some channel ->
            match f channel with
            | newData, cmd when newData = channel && cmd.IsEmpty -> chat, Cmd.none
            | newData, cmd ->
                { chat with Channels = chat.Channels |> Map.add chanId newData },
                  cmd |> Cmd.map (fun x -> Types.ChannelMsg (chanId, x))
        | None ->
            console.error ("Channel %s update failed - channel not found", chanId)
            chat, Cmd.none

    let updateChan chanId (f: ChannelData -> ChannelData) (chat: ChatData) : ChatData =
        match chat.Channels |> Map.tryFind chanId with
        | Some channel ->
            match f channel with
            | newData when newData = channel -> chat
            | newData -> { chat with Channels = chat.Channels |> Map.add chanId newData }
        | None ->
            console.error ("Channel %s update failed - channel not found", chanId)
            chat

    let mutable lastRequestId = 10000
    let toCommand x =
        let reqId = lastRequestId.ToString()
        lastRequestId <- lastRequestId + 1
        Protocol.ServerMsg.ServerCommand (reqId, x)

    let applicationMsgUpdate (msg: AppMsg) (state: ChatData) :(ChatData * Msg Cmd) =

        match msg with
        | Nop -> state, Cmd.none

        | ChannelMsg (chanId, Forward text) ->
            let message =
                match text with
                | cmd when cmd.StartsWith "/" -> Protocol.UserCommand {command = cmd; chan = chanId} |> toCommand
                | _ -> Protocol.UserMessage {text = text; chan = chanId}

            state, Cmd.ofSocketMessage state.socket message

        | ChannelMsg (chanId, Msg.Leave) ->
            state, Cmd.ofSocketMessage state.socket (Protocol.Leave chanId |> toCommand)

        | ChannelMsg (chanId, msg) ->
            let newState, cmd = state |> updateChanCmd chanId (Channel.State.update msg)
            newState, Cmd.map ApplicationMsg cmd

        | SetNewChanName name ->
            { state with NewChanName = name }, Cmd.none
            
        | CreateJoin ->
            match state.NewChanName with
            | Some channelName ->
                state, Cmd.batch
                        [ Cmd.ofSocketMessage state.socket (Protocol.JoinOrCreate channelName |> toCommand)
                          Cmd.ofMsg <| SetNewChanName None |> Cmd.map ApplicationMsg]
            | None -> state, Cmd.none
        | Join chanId ->
            state, Cmd.ofSocketMessage state.socket (Protocol.Join chanId |> toCommand)
        | Leave chanId ->
            state, Cmd.ofSocketMessage state.socket (Protocol.Leave chanId |> toCommand)

    let unknownUser userId = {
        Id = userId; Nick = "Unknown #" + userId; Status = ""
        IsBot = false; Online = true; ImageUrl = None; isMe = false}

    let getUser (userId: string) (users: Map<UserId,UserInfo>) : UserInfo =
        users |> Map.tryFind userId |> Core.Option.defaultWith (fun () -> unknownUser userId)

    let joinChannel channel chat =
        // TODO this should put channel to a Connecting state
        let chanData, cmd =
            Channel.State.init() |> fst
            |> Channel.State.update (Init (channel, [], []))
        {chat with Channels = chat.Channels |> Map.add channel.Id chanData}, cmd

    let updateChannelData isMe channel (chanData: Protocol.ActiveChannelData) chat =
        let users = chanData.users |> List.map (Conversions.mapUserInfo isMe)
        let messages = chanData.lastMessages |> List.sortBy(fun msg -> msg.id) |> List.map Conversions.mapUserMessage
        
        let chanData, cmd =
            fst (Channel.State.init()) |> Channel.State.update (Init (channel, users, messages))
        {chat with Channels = chat.Channels |> Map.add channel.Id chanData}, cmd

    let chatUpdate isMe (msg: Protocol.ClientMsg) (state: ChatData) : ChatData * Cmd<Msg> =
    
        let mapCmd f (state, cmd) = state, cmd |> Cmd.map f
        let ignoreCmd (state, _) = state, Cmd.none

        match msg with
        | Protocol.ClientMsg.ChanMsg msg ->
            let channelCmd = msg |> Conversions.mapUserMessage |> AppendUserMessage
            state |> updateChanCmd msg.chan (Channel.State.update channelCmd) |> mapCmd ApplicationMsg

        | Protocol.ClientMsg.ServerEvent { evt = Protocol.ChannelEvent (chan, evt) } ->

            let userInfo user = Conversions.mapUserInfo isMe user

            let chan, message =
                evt |> function
                | Protocol.Joined user  -> chan, Channel.Types.UserJoined (userInfo user)
                | Protocol.Left userid  -> chan, Channel.Types.UserLeft userid
                | Protocol.Updated user -> chan, Channel.Types.UserUpdated (userInfo user)

            updateChanCmd chan (Channel.State.update message) state |> ignoreCmd

        | Protocol.ClientMsg.ServerEvent { evt = Protocol.NewChannel chan } ->
            { state with ChannelList = state.ChannelList |> Map.add chan.id (Conversions.mapChannel chan)}, Cmd.none

        | Protocol.ClientMsg.ServerEvent { evt = Protocol.RemoveChannel chan } ->
            { state with ChannelList = state.ChannelList |> Map.remove chan.id }, Cmd.none

        | Protocol.ClientMsg.ServerEvent { evt = Protocol.JoinedChannel chanData } ->
            let chanInfo = state.ChannelList.[chanData.channelId]
            in
            updateChannelData isMe chanInfo chanData state |> mapCmd (fun msg -> ChannelMsg (chanData.channelId, msg) |> ApplicationMsg)

        | notProcessed ->
            printfn "message was not processed: %A" notProcessed
            state, Cmd.none

    let socketMsgUpdate msg =
        function
        | Connected (me, chat) as state ->
            let isMe = (=) me.Id
            match msg with

            | Protocol.Hello hello ->
                let me = Conversions.mapUserInfo ((=) hello.me.id) hello.me
                let channels = hello.channels |> List.map (fun ch -> ch.id, Conversions.mapChannel ch) |> Map.ofList
                in
                Connected (me, { ChatData.Empty with socket = chat.socket; ChannelList = channels }), Cmd.batch []

            | Protocol.CmdResponse (reqId, reply) ->
                match reply with
                | Protocol.UserUpdated newUser ->
                    let meNew = Conversions.mapUserInfo isMe newUser
                    in
                    Connected (meNew, chat), Cmd.none

                | Protocol.CommandResponse.JoinedChannel chanInfo ->
                    let newState, cmd = chat |> joinChannel (Conversions.mapChannel chanInfo)
                    in
                    Connected (me, newState), Cmd.batch [
                          cmd |> Cmd.map (fun msg -> ChannelMsg (chanInfo.id, msg) |> ApplicationMsg)
                          Channel chanInfo.id |> toHash |> Navigation.newUrl ]

                | Protocol.LeftChannel channelId ->
                    chat.Channels |> Map.tryFind channelId
                    |> function
                    | Some _ ->
                        Connected (me, {chat with Channels = chat.Channels |> Map.remove channelId}),
                            Overview |> toHash |> Navigation.newUrl
                    | _ ->
                        printfn "Channel not found %s" channelId
                        state, Cmd.none

                | Protocol.Pong ->
                    console.debug <| sprintf "Pong %s" reqId
                    state, Cmd.none

                | Protocol.Error error ->
                    console.error <| sprintf "Server replied with error %A" error    // FIXME report error to user
                    state, Cmd.none

            | protocolMsg ->
                let chatData, cmd = chatUpdate isMe protocolMsg chat
                Connected (me, chatData), cmd
        | other ->
            console.info <| sprintf "Socket message %A" other
            (other, Cmd.none)

open Implementation

let init () : ChatState * Cmd<Msg> =
    let socketAddr = sprintf "ws://%s/api/socket" location.host
    console.debug ("Opening socket at '%s'", socketAddr)
    NotConnected, Cmd.tryOpenSocket socketAddr

let update msg state : ChatState * Cmd<Msg> = 
    match msg with
    | ApplicationMsg amsg ->
        match state with
        | Connected (me, chat) ->
            let newChat, cmd = applicationMsgUpdate amsg chat
            Connected(me, newChat), cmd
        | _ ->
            console.error <| "Failed to process channel message. Server is not connected"
            state, Cmd.none
    | WebsocketMsg (socket, Opened) ->
        Connected (UserInfo.Anon, { ChatData.Empty with socket = socket }), Cmd.ofSocketMessage socket Protocol.ServerMsg.Greets
    | WebsocketMsg (_, Msg socketMsg) ->
        socketMsgUpdate socketMsg state
    | _ -> (state, Cmd.none)
