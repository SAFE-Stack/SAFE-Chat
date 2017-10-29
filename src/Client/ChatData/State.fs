module ChatData.State

open Elmish
open Fable.PowerPack
open Fable.PowerPack.Fetch.Fetch_types

open Types

type private WhoPayload = {Id: string; UserId: string; Nickname: string}

let getUserInfo () =
    promise {
        let props =
            [Credentials RequestCredentials.Include]
        let! response = Fetch.fetchAs<WhoPayload> "/api/who" props
        return {Nick = response.Nickname; UserId = response.UserId}
    }

let loadUserInfoCmd = 
    Cmd.ofPromise getUserInfo () Connected FetchError
    
let init () : Chat * Cmd<Msg> =
  NotConnected, loadUserInfoCmd


let update msg _ =
    match msg with
    | Connected x ->
        // TODO expect
        let data = {Me = x; Channels = Map.empty; Users = Map.empty}
        data, []
    | Reset ->        NotLoggedIn, []
    | FetchError x -> Error x, []
