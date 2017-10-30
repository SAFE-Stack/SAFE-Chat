module UserInfo.State

open Elmish
open Fable.PowerPack
open Fable.PowerPack.Fetch.Fetch_types

open Types

type private WhoPayload = {Id: string; UserId: string; Nickname: string}

let getUserInfo () =
    promise {
        let props =
            [Credentials RequestCredentials.Include]
        let! response = Fetch.fetchAs<WhoPayload> "/api/hello" props
        return {Nick = response.Nickname; UserId = response.UserId}
    }

let loadUserInfoCmd = 
    Cmd.ofPromise getUserInfo () Update FetchError
    
let init () : UserInfo * Cmd<Msg> =
  NotLoggedIn, loadUserInfoCmd

let update msg _ =
  match msg with
  | Update x ->     UserInfo x, []
  | Reset ->        NotLoggedIn, []
  | FetchError x -> Error x, []
