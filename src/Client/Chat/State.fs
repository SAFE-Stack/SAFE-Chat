module Chat.State

open Elmish
open Elmish.Browser.Navigation
open Fable.PowerPack
open Fetch.Fetch_types

open Fable.Websockets.Elmish
open Fable.Websockets.Client
open Fable.Websockets.Protocol

open Router
open Types

open FsChat

module Conversions =

    let mapChannel (ch: Protocol.ChannelInfo): ChannelData =
        {Id = ch.id; Name = ch.name; Topic = ch.topic; Users = UserCount ch.userCount; Messages = []; Joined = ch.joined}

module Commands =

    let getUserInfo () =
        promise {
            let props = [Method HttpMethod.POST; Credentials RequestCredentials.Include]
            let! response = Fetch.fetchAs<Protocol.Hello> "/api/hello" props
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

let init () : ChatState * Cmd<MsgType> =
  NotConnected, Cmd.tryOpenSocket "ws://localhost:8083/websocket"

let applicationMsgUpdate (msg: Msg) state: (ChatState * MsgType Cmd) =
    match msg with
    | Nop -> state, Cmd.none
    | Hello (me, channels) ->
        let data = {ChatData.Empty with Channels = channels |> List.map (fun ch -> ch.Id, ch) |> Map.ofList}
        Connected (me, data), Cmd.none

    | SetNewChanName name ->
        let (Connected (me, state)) = state
        Connected (me, {state with NewChanName = name }), Cmd.none
        
    | CreateJoin ->
        let (Connected (me, state)) = state
        Connected (me, state), Cmd.batch
                [ createJoinChannelCmd state.NewChanName
                  Cmd.ofMsg <| SetNewChanName ""]
    | Join chanId ->
        state, joinChannelCmd chanId
    | Joined chan ->
        let (Connected (me, state)) = state
        Connected (me, {state with Channels = state.Channels |> Map.add chan.Id chan}), Navigation.newUrl  <| toHash (Channel chan.Id)
    | Leave chanId ->
        state, Cmd.batch [ leaveChannelCmd chanId
                           Navigation.newUrl  <| toHash Home]
    
    | Left chanId ->
        printfn "Left %s" chanId
        let (Connected (me, state)) = state
        let updateChannel id f = Map.map (fun k v -> if k = id then f v else v)
        let setJoined v ch = {ch with Joined = v}
        Connected (me, {state with Channels = state.Channels |> updateChannel chanId (setJoined false)}), Cmd.none
    |> function | (state, cmd) -> state, Cmd.map ApplicationMsg cmd

let inline update msg prevState = 
    match msg with
    | ApplicationMsg amsg -> applicationMsgUpdate amsg prevState
    | WebsocketMsg (socket, Opened) ->
        Connected (UserInfo.Anon, { ChatData.Empty with socket = socket }), Cmd.none
    | WebsocketMsg (_, Msg socketMsg) -> prevState, Cmd.none  // TODO (socketMsgUpdate socketMsg prevState)
    | _ -> (prevState, Cmd.none)

(*
    | Reset ->        NotLoggedIn, []
    | FetchError x -> Error x, []
*)
