module ChatData.State

open Elmish
open Fable.PowerPack
open Fable.PowerPack.Fetch.Fetch_types

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
        {Id = ch.id; Name = ch.name; Topic = ch.topic; Users = []; Messages = []}

let getUserInfo () =
    promise {
        let props =
            [Credentials RequestCredentials.Include]
        let! response = Fetch.fetchAs<Payloads.Hello> "/api/hello" props
        let channels = response.channels |> List.map Conversions.mapChannel
        return {Nick = response.nickname; UserId = response.userId}, channels
    }

let joinChannel chanName =
    promise {
        let props =
            [Credentials RequestCredentials.Include]
        let! response = Fetch.fetchAs<Payloads.ChannelInfo> (sprintf "/api/%s/join" chanName) props
        return response |> Conversions.mapChannel
    }

let loadUserInfoCmd = 
    Cmd.ofPromise getUserInfo () Connected FetchError

let joinChannelCmd chan = 
    Cmd.ofPromise joinChannel chan Joined FetchError
    
let init () : Chat * Cmd<Msg> =
  NotConnected, loadUserInfoCmd

let update msg state =
    match msg with
    | Connected (me, channels) ->
        let data = {
            Me = me
            Channels = channels |> List.map (fun ch -> ch.Id, ch) |> Map.ofList
            Users = Map.empty}
        ChatData data, []
    | Join chanName ->
        // FIXME verify already joined
        state, joinChannelCmd chanName
    | Joined chan ->
        let (ChatData state) = state
        ChatData {state with Channels = state.Channels |> Map.add chan.Id chan}, []
(*
    | Reset ->        NotLoggedIn, []
    | FetchError x -> Error x, []
*)
