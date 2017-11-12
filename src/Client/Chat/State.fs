module Chat.State

open Elmish
open Elmish.Browser.Navigation
open Fable.PowerPack
open Fetch.Fetch_types

open Fable.Websockets.Elmish
open Fable.Websockets.Protocol

open Router
open Types

open FsChat

module Conversions =

    let mapChannel (ch: Protocol.ChannelInfo): ChannelData =
        {Id = ch.id; Name = ch.name; Topic = ch.topic; Users = UserCount ch.userCount; Messages = []; Joined = ch.joined; PostText = ""}

module Commands =

    let getUserInfo () =
        promise {
            let props = [Method HttpMethod.POST; Credentials RequestCredentials.Include]
            let! response = Fetch.fetchAs<Protocol.HelloInfo> "/api/hello" props
            let channels = response.channels |> List.map Conversions.mapChannel
            return {Nick = response.nickname; Email = None; UserId = response.userId}, channels
        }

    let joinChannel chanId =
        promise {
            let props = [Method HttpMethod.POST; Credentials RequestCredentials.Include]
            let! response = Fetch.fetchAs<Protocol.ChannelInfo> (sprintf "/api/channel/%s/join" chanId) props
            return response |> Conversions.mapChannel
        }

    let createJoinChannel chanName =
        promise {
            let props = [Method HttpMethod.POST; Credentials RequestCredentials.Include]
            let! response = Fetch.fetchAs<Protocol.ChannelInfo> (sprintf "/api/channel/%s/joincreate" chanName) props
            return response |> Conversions.mapChannel
        }

    let leaveChannel chanId =
        promise {
            let props = [Credentials RequestCredentials.Include]
            let! _ = Fetch.postRecord (sprintf "/api/channel/%s/leave" chanId) () props
            return chanId
        }

    let loadUserInfoCmd = Cmd.ofPromise getUserInfo () Hello FetchError
    let joinChannelCmd chan = Cmd.ofPromise joinChannel chan Joined FetchError
    let createJoinChannelCmd chan = Cmd.ofPromise createJoinChannel chan Joined FetchError
    let leaveChannelCmd chan = Cmd.ofPromise leaveChannel chan Left FetchError

open Commands
open System.Runtime.InteropServices.ComTypes

let init () : ChatState * Cmd<MsgType> =
  NotConnected, Cmd.tryOpenSocket "ws://localhost:8083/api/socket"

let applicationMsgUpdate (msg: AppMsg) state: (ChatState * MsgType Cmd) =

    let updateChannel chanId f s = {s with Channels = s.Channels |> Map.map (fun k v -> if k = chanId then (f v) else v)}

    let updateChan chanId f =
        let (Connected (me, state)) = state
        Connected (me, state |> updateChannel chanId f)

    let setText v ch = {ch with PostText = v}
    let setJoined v ch = {ch with Joined = v}

    match msg with
    | Nop -> state, Cmd.none
    | SetPostText (chanId, text) ->
        // let newState = updateChan chanId (setText text)
        let (Connected (me, state)) = state
        let f = (setText text)
        let newState = Connected (me,
            {state with Channels = state.Channels |> Map.map (fun k v -> if k =chanId then (f v) else v)})

        printfn "ST %A" newState
        newState, Cmd.none

    | PostText chanId ->
        let (Connected (_, chat)) = state
        let message = chat.Channels |> Map.tryFind chanId |> Option.map (fun ch -> ch.PostText)
        match message with
        | Some text ->
            let userMessage = Protocol.UserMessage {id = 1; ts = System.DateTime.Now; text = text; chan = chanId; author = "xxx"}
            updateChan chanId (setText ""), Cmd.ofSocketMessage chat.socket userMessage
        | _ ->
            state, Cmd.none

    | Hello (me, channels) ->
        let data = {ChatData.Empty with Channels = channels |> List.map (fun ch -> ch.Id, ch) |> Map.ofList}
        Connected (me, data), Cmd.none

    | SetNewChanName name ->
        let (Connected (me, state)) = state
        Connected (me, {state with NewChanName = name }), Cmd.none
        
    | CreateJoin ->
        let (Connected (me, state)) = state
        Connected (me, state), Cmd.batch
                [ createJoinChannelCmd state.NewChanName |> Cmd.map ApplicationMsg
                  Cmd.ofMsg <| SetNewChanName "" |> Cmd.map ApplicationMsg]
    | Join chanId ->
        state, joinChannelCmd chanId |> Cmd.map ApplicationMsg
    | Joined chan ->
        let (Connected (me, state)) = state
        Connected (me, {state with Channels = state.Channels |> Map.add chan.Id chan}), Navigation.newUrl  <| toHash (Channel chan.Id)
    | Leave chanId ->
        state, Cmd.batch [ leaveChannelCmd chanId |> Cmd.map ApplicationMsg
                           Navigation.newUrl  <| toHash Home |> Cmd.map ApplicationMsg]
    
    | Left chanId ->
        printfn "Left %s" chanId
        updateChan chanId (setJoined false), Cmd.none
    
    | Disconnected ->
        NotConnected, Cmd.none

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
            let me = { UserInfo.Anon with Nick = hello.nickname; UserId = hello.userId }
            Connected (me, chatData), Cmd.none
        | protocolMsg ->
            let chatData, cmds = chatUpdate protocolMsg prevChatState
            Connected (me, chatData), cmds
    | other ->
        printfn "Socket message %A" other
        (prevState, Cmd.none)

let inline update msg prevState = 
    match msg with
    | ApplicationMsg amsg -> applicationMsgUpdate amsg prevState
    | WebsocketMsg (socket, Opened) ->
        Connected (UserInfo.Anon, { ChatData.Empty with socket = socket }), Cmd.none
    | WebsocketMsg (_, Msg socketMsg) ->
        socketMsgUpdate socketMsg prevState
    | _ -> (prevState, Cmd.none)

(*
    | Reset ->        NotLoggedIn, []
    | FetchError x -> Error x, []
*)
