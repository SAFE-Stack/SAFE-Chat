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
        {Nick = u.nick; Name = u.nick; Email = u.email; IsBot = u.isbot; Online = u.online}

    let mapChannel (ch: Protocol.ChannelInfo): ChannelData =
        let usersInfo =
            match ch.userCount, ch.users with
            | cnt, [] -> UserCount cnt
            | _, lst -> lst |> List.map (fun ch -> ch.nick, mapUserInfo ch) |> Map.ofList |> UserList
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
        | ChannelMsg (chanId, Forward msg) ->
            state, Cmd.ofSocketMessage chat.socket (Protocol.UserMessage {msg with chan = chanId})
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
            state, Cmd.batch
                    [ Cmd.ofSocketMessage chat.socket (Protocol.JoinOrCreate chat.NewChanName)
                      Cmd.ofMsg <| SetNewChanName "" |> Cmd.map ApplicationMsg]
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

let appendMessage (msg: Protocol.ChannelMsg) (chan: ChannelData) =
    let newMessage: Message =
      { Id = msg.id; AuthorId = msg.author; Ts = msg.ts
        Text = msg.text }
    {chan with Messages = chan.Messages @ [newMessage]}

let chatUpdate (msg: Protocol.ClientMsg) (state: ChatData) : ChatData * Cmd<MsgType> =
    match msg with
    | Protocol.ClientMsg.ChanMsg chanMsg ->
        updateChan chanMsg.chan (appendMessage chanMsg) state, Cmd.none
    | _ ->
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
            let me: UserInfo = { Nick = hello.nick; Name = hello.name; Email = hello.email; Online = true; IsBot = false }
            Connected (me, chatData), Cmd.none
        | Protocol.ClientMsg.JoinedChannel chanInfo ->
            Connected (me,
                {prevChatState with
                    Channels = prevChatState.Channels |> Map.add chanInfo.id (Conversions.mapChannel chanInfo)}),
                Navigation.newUrl  <| toHash (Channel chanInfo.id)
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
        Connected (UserInfo.Anon, { ChatData.Empty with socket = socket }), Cmd.none
    | WebsocketMsg (_, Msg socketMsg) ->
        socketMsgUpdate socketMsg prevState
    | _ -> (prevState, Cmd.none)

(*
    | Reset ->        NotLoggedIn, []
    | FetchError x -> Error x, []
*)
