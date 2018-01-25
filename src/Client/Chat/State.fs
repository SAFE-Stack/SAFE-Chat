module Chat.State

open Elmish
open Elmish.Browser.Navigation

open Fable.Import
open Fable.Websockets.Elmish
open Fable.Websockets.Protocol
open Fable.Websockets.Elmish.Types

open Router

open Channel.Types
open Chat.Types

open FsChat

module Conversions =

    let mapUserInfo isMe (u: Protocol.ChanUserInfo) :UserInfo =
        { Id = u.id; Nick = u.nick; IsBot = u.isbot
          Status = u.status
          Online = true; ImageUrl = Option.ofObj u.imageUrl
          isMe = isMe u.id}

    let mapChannel isMe (ch: Protocol.ChannelInfo) : ChannelData =
        let usersInfo =
            match ch.userCount, ch.users with
            | cnt, [] -> UserCount cnt
            | _, lst -> lst |> List.map (fun u -> u.id, mapUserInfo isMe u) |> Map.ofList |> UserList
        {Id = ch.id; Name = ch.name; Topic = ch.topic; Users = usersInfo; Messages = []; Joined = ch.joined; PostText = ""}

let init () : ChatState * Cmd<MsgType> =
  NotConnected, Cmd.tryOpenSocket <| sprintf "ws://%s/api/socket" Browser.location.host

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
    let fallback () = {
        Id = userId; Nick = "Unknown #" + userId; Status = ""
        IsBot = false; Online = true; ImageUrl = None; isMe = false}

    match users with
    | UserCount _ -> fallback()
    | UserList map ->
        match map |> Map.tryFind userId with
        | Some userInfo -> userInfo
        | None -> fallback()

let appendMessage makeMessage (chan: ChannelData) =
    {chan with Messages = chan.Messages @ [makeMessage chan]}

let appendSysMsg (ev: Protocol.UserEventInfo) content (chan: ChannelData) =
    let newMessage: Message = { Id = ev.id; Ts = ev.ts; Content = SystemMessage <| content}
    {chan with Messages = chan.Messages @ [newMessage]}

let getChannelUser userid (chan: ChannelData) =
    match chan.Users with
        | UserCount _ -> None
        | UserList l -> l |> Map.tryFind userid
let getChanUserNick state chanid userid =
    state.Channels |> Map.tryFind chanid
    |> Option.bind (getChannelUser userid)
    |> Option.map (fun user -> user.Nick)
    
let chatUpdate isMe (msg: Protocol.ClientMsg) (state: ChatData) : ChatData * Cmd<MsgType> =
    match msg with
    | Protocol.ClientMsg.ChanMsg msg ->
        let message chan =
            { Id = msg.id; Ts = msg.ts
              Content = UserMessage (msg.text, getUser msg.author chan.Users ) }
        updateChan msg.chan (appendMessage message) state, Cmd.none

    | Protocol.ClientMsg.UserEvent ev ->

        let processUserEvent chan countAction listAction updateAction =
            updateChan chan (updateUsers <| function
                | UserCount c -> UserCount <| countAction c
                | UserList m -> listAction m |> UserList
                >> updateAction
                ) state, Cmd.none
        let user = Conversions.mapUserInfo isMe ev.user

        match ev.evt with
        | Protocol.Joined chan ->

            processUserEvent
                chan ((+) 1) (Map.add ev.user.id user)
                (appendSysMsg ev <| sprintf "%s joined the channel" ev.user.nick)

        | Protocol.Updated chan ->
            let appendMessage = getChanUserNick state chan ev.user.id |> function
                | Some newnick when newnick <> ev.user.nick ->
                    let txt = sprintf "%s is now known as %s" newnick ev.user.nick
                    (appendSysMsg ev txt)
                | _ -> id

            processUserEvent
                chan id (Map.add ev.user.id user)
                appendMessage

        | Protocol.Left chan ->
            processUserEvent
                chan id (Map.remove ev.user.id)
                (appendSysMsg ev <| sprintf "%s left the channel" ev.user.nick)

    | Protocol.ClientMsg.NewChannel chan ->
        { state with Channels = state.Channels |> Map.add chan.id (Conversions.mapChannel isMe chan)}, Cmd.none

    | Protocol.ClientMsg.RemoveChannel chan ->
        { state with Channels = state.Channels |> Map.remove chan.id }, Cmd.none

    | notProcessed ->
        printfn "message was not processed: %A" notProcessed
        state, Cmd.none

let socketMsgUpdate (msg: Protocol.ClientMsg) prevState : ChatState * Cmd<MsgType> =
    let isMe =
        match prevState with
        | Connected (me,_) -> (=) me.Id
        | _ -> fun _ -> false
    match prevState with
    | Connected (me, prevChatState) ->
        match msg with

        | Protocol.ClientMsg.Hello hello ->
            let me = Conversions.mapUserInfo ((=) hello.me.id) hello.me
            let chatData =
              { ChatData.Empty with
                    socket = prevChatState.socket
                    Channels = hello.channels |> List.map (fun ch -> ch.id, Conversions.mapChannel isMe ch) |> Map.ofList
                    }
            Connected (me, chatData), Cmd.none

        | Protocol.ClientMsg.UserUpdated newUser ->
            let meNew = Conversions.mapUserInfo isMe newUser
            Connected (meNew, prevChatState), Cmd.none

        | Protocol.ClientMsg.JoinedChannel chanInfo ->
            let chanData = Conversions.mapChannel isMe chanInfo
            Connected (me,
                {prevChatState with
                    Channels = prevChatState.Channels |> Map.add chanInfo.id chanData}),
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
            let chatData, cmds = chatUpdate isMe protocolMsg prevChatState
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
