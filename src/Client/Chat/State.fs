module Chat.State

open Elmish
open Elmish.Browser.Navigation

open Fable.Websockets.Elmish
open Fable.Websockets.Protocol
open Fable.Websockets.Elmish.Types

open Router

open Channel.Types
open Chat.Types

open FsChat

module Conversions =

    let mapUserInfo (u: Protocol.ChanUserInfo): UserInfo =
        {Id = u.id; Nick = u.nick; IsBot = u.isbot; Online = true; ImageUrl = Option.ofObj u.imageUrl}

    let mapChannel (ch: Protocol.ChannelInfo): ChannelData =
        let usersInfo =
            match ch.userCount, ch.users with
            | cnt, [] -> UserCount cnt
            | _, lst -> lst |> List.map (fun u -> u.id, mapUserInfo u) |> Map.ofList |> UserList
        {Id = ch.id; Name = ch.name; Topic = ch.topic; Users = usersInfo; Messages = []; Joined = ch.joined; PostText = ""}

open Fable.Import

let init () : ChatState * Cmd<MsgType> =
  NotConnected, Cmd.tryOpenSocket "ws://localhost:8083/api/socket"

let applicationMsgUpdate (msg: AppMsg) state: (ChatState * MsgType Cmd) =

    let updateChannel chanId f s = {s with Channels = s.Channels |> Map.map (fun k v -> if k = chanId then (f v) else v)}
    let setJoined v ch = {ch with Joined = v}

    match state with
    | Connected (me, chat) ->
        match msg with
        | Nop -> state, Cmd.none

        | ChannelMsg (chanId, Forward text) ->
            let message : string -> _ = function
                | text when text.StartsWith "/" -> Protocol.ControlCommand | _ -> Protocol.UserMessage

            state, Cmd.ofSocketMessage chat.socket (message text {text = text; chan = chanId})

        | ChannelMsg (chanId, Msg.Leave) ->
            state, Cmd.ofSocketMessage chat.socket (Protocol.ServerMsg.Leave chanId)

        | ChannelMsg (chanId, msg) ->
            match chat.Channels |> Map.tryFind chanId with
            | Some prevChan ->
                let chan, cmd = Channel.State.update msg prevChan
                Connected (me, { chat with Channels = chat.Channels |> Map.add chanId chan }),
                    cmd |> Cmd.map (fun c -> ChannelMsg (chanId, c) |> ApplicationMsg)
            | _ ->
                Browser.console.error <| sprintf "Failed to process channel message. Channel '%s' not found" chanId
                state, Cmd.none

        | SetNewChanName name ->
            Connected (me, {chat with NewChanName = name }), Cmd.none
            
        | CreateJoin ->
            match chat.NewChanName with
            | Some channelName ->
                state, Cmd.batch
                        [ Cmd.ofSocketMessage chat.socket (Protocol.JoinOrCreate channelName)
                          Cmd.ofMsg <| SetNewChanName None |> Cmd.map ApplicationMsg]
            | None -> state, Cmd.none
        | Join chanId ->
            state, Cmd.ofSocketMessage chat.socket (Protocol.Join chanId)
        | Leave chanId ->
            state, Cmd.ofSocketMessage chat.socket (Protocol.Leave chanId)

        // TODO filter whatever is left below

        | Left chanId ->
            Connected (me, chat |> updateChannel chanId (setJoined false)), Cmd.none

    | _ ->
        Browser.console.error <| "Failed to process channel message. Server is not connected"
        state, Cmd.none
   

let updateChan chanId (f: ChannelData -> ChannelData) (chat: ChatData) : ChatData =
    let update cid = if cid = chanId then f else id
    { chat with Channels = chat.Channels |> Map.map update }

let updateUsers (f: UsersInfo -> UsersInfo) =
    (fun ch -> { ch with Users = f ch.Users})

let private getUser (userId: string) (users: UsersInfo) : UserInfo =
    let fallback () = {Id = userId; Nick = "Unknown #" + userId; IsBot = false; Online = true; ImageUrl = None}

    match users with
    | UserCount _ -> fallback()
    | UserList map ->
        match map |> Map.tryFind userId with
        | Some userInfo -> userInfo
        | None -> fallback()

let appendMessage (msg: Protocol.ChannelMsgInfo) (chan: ChannelData) =
    let newMessage: Message = { Id = msg.id; Ts = msg.ts; Content = UserMessage (msg.text, getUser msg.author chan.Users ) }
    {chan with Messages = chan.Messages @ [newMessage]}

let appendSysMessage verb (ev: Protocol.UserEventRec) (chan: ChannelData) =
    let newMessage: Message = { Id = ev.id; Ts = ev.ts; Content = SystemMessage <| sprintf "%s %s the channel" ev.user.nick verb}
    {chan with Messages = chan.Messages @ [newMessage]}

let chatUpdate (msg: Protocol.ClientMsg) (state: ChatData) : ChatData * Cmd<MsgType> =
    match msg with
    | Protocol.ClientMsg.ChanMsg chanMsg ->
        updateChan chanMsg.chan (appendMessage chanMsg) state, Cmd.none

    | Protocol.ClientMsg.UserJoined (ev, chan) ->
        updateChan chan (updateUsers <| function
            | UserCount c -> UserCount (c + 1)
            | UserList m -> m |> Map.add ev.user.id (Conversions.mapUserInfo ev.user) |> UserList
            >> appendSysMessage "joined" ev
            ) state, Cmd.none

    | Protocol.ClientMsg.UserLeft (ev, chan) ->
        updateChan chan (updateUsers <| function
            | UserCount c -> UserCount (if c > 0 then c - 1 else 0)
            | UserList m -> m |> Map.remove ev.user.id |> UserList
            >> appendSysMessage "left" ev
            ) state, Cmd.none

    | Protocol.ClientMsg.NewChannel chan ->
        { state with Channels = state.Channels |> Map.add chan.id (Conversions.mapChannel chan)}, Cmd.none

    | Protocol.ClientMsg.RemoveChannel chan ->
        { state with Channels = state.Channels |> Map.remove chan.id }, Cmd.none

    | notProcessed ->
        printfn "message was not processed: %A" notProcessed
        state, Cmd.none

let socketMsgUpdate (msg: Protocol.ClientMsg) prevState : ChatState * Cmd<MsgType> =
    match prevState with
    | Connected (me, prevChatState) ->
        match msg with

        | Protocol.ClientMsg.Hello hello ->
            let chatData =
              { ChatData.Empty with
                    socket = prevChatState.socket
                    Channels = hello.channels |> List.map (fun ch -> ch.id, Conversions.mapChannel ch) |> Map.ofList
                    }
            let me = Conversions.mapUserInfo hello.me
            Connected (me, chatData), Cmd.none

        | Protocol.ClientMsg.JoinedChannel chanInfo ->
            Connected (me,
                {prevChatState with
                    Channels = prevChatState.Channels |> Map.add chanInfo.id (Conversions.mapChannel chanInfo)}),
                Channel chanInfo.id |> toHash |> Navigation.newUrl
        | Protocol.ClientMsg.LeftChannel channelId ->
            let chan = prevChatState.Channels |> Map.tryFind channelId
            match chan with
            | Some chanInfo ->
                Connected (me,
                    {prevChatState with
                        Channels = prevChatState.Channels |> Map.add channelId {chanInfo with Joined = false}}),
                    About |> toHash |> Navigation.newUrl
            | _ ->
                printfn "Channel not found %s" channelId
                prevState, Cmd.none
        | protocolMsg ->
            let chatData, cmds = chatUpdate protocolMsg prevChatState
            Connected (me, chatData), cmds
    | other ->
        printfn "Socket message %A" other
        (prevState, Cmd.none)

let inline update msg prevState = 
    match msg with
    | ApplicationMsg amsg ->
        applicationMsgUpdate amsg prevState
    | WebsocketMsg (socket, Opened) ->
        Connected (UserInfo.Anon, { ChatData.Empty with socket = socket }), Cmd.ofSocketMessage socket Protocol.ServerMsg.Greets
    | WebsocketMsg (_, Msg socketMsg) ->
        socketMsgUpdate socketMsg prevState
    | _ -> (prevState, Cmd.none)
