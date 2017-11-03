module ChatData.State

open Elmish
open Elmish.Browser.Navigation
open Fable.PowerPack
open Fable.PowerPack.Fetch.Fetch_types

open Router
open Types

module Payloads =

    // TODO this is a copy of server's ChatApi payloads, so the code should be shared

    type ChanUserInfo = {
        name: string; online: bool; isbot: bool; lastSeen: System.DateTime
    }
    type ChannelInfo = {
        id: string; name: string; userCount: int; topic: string; joined: bool; users: ChanUserInfo list
    }

    type Hello = {
        userId: string; nickname: string
        channels: ChannelInfo list
    }

module Conversions =

    let mapChannel (ch: Payloads.ChannelInfo): ChannelData =
        {Id = ch.id; Name = ch.name; Topic = ch.topic; Users = UserCount ch.userCount; Messages = []; Joined = ch.joined}

let getUserInfo () =
    promise {
        let props = [Method HttpMethod.POST; Credentials RequestCredentials.Include]
        let! response = Fetch.fetchAs<Payloads.Hello> "/api/hello" props
        let channels = response.channels |> List.map Conversions.mapChannel
        return {Nick = response.nickname; UserId = response.userId}, channels
    }

let joinChannel chanId =
    promise {
        let props = [Method HttpMethod.POST; Credentials RequestCredentials.Include]
        let! response = Fetch.fetchAs<Payloads.ChannelInfo> (sprintf "/api/channel/%s/join" chanId) props
        return response |> Conversions.mapChannel
    }

let leaveChannel chanId =
    promise {
        let props = [Credentials RequestCredentials.Include]
        let! _ = Fetch.postRecord (sprintf "/api/channel/%s/leave" chanId) () props
        return chanId
    }

let loadUserInfoCmd = 
    Cmd.ofPromise getUserInfo () Connected FetchError

let joinChannelCmd chan = 
    Cmd.ofPromise joinChannel chan Joined FetchError

let leaveChannelCmd chan = Cmd.ofPromise leaveChannel chan Left FetchError
    
let init () : Chat * Cmd<Msg> =
  NotConnected, loadUserInfoCmd

let update msg state =
    match msg with
    | Nop -> state, []
    | Connected (me, channels) ->
        let data = {
            Me = me
            Channels = channels |> List.map (fun ch -> ch.Id, ch) |> Map.ofList
            Users = Map.empty}
        ChatData data, []
    | Join chanId ->
        state, joinChannelCmd chanId
    | Joined chan ->
        let (ChatData state) = state
        ChatData {state with Channels = state.Channels |> Map.add chan.Id chan}, Navigation.newUrl  <| toHash (Channel chan.Id)
    | Leave chanId ->
        state, Cmd.batch [ leaveChannelCmd chanId
                           Navigation.newUrl  <| toHash Home]
    
    | Left chanId ->
        printfn "Left %s" chanId
        let (ChatData state) = state
        let updateChannel id f =
            Map.map (fun k v -> if k = id then f v else v)
        ChatData {state with Channels = state.Channels |> updateChannel chanId (fun ch -> {ch with Joined = false})}, []

(*
    | Reset ->        NotLoggedIn, []
    | FetchError x -> Error x, []
*)
