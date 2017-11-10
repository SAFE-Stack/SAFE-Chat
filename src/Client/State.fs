module App.State

open Elmish
open Elmish.Browser.Navigation
open Fable.Websockets.Elmish
open Fable.Import.Browser
open Router
open Types

let urlUpdate (result: Option<Route>) model =
    match result with
    | None ->
        console.error("Error parsing url")
        model, Navigation.modifyUrl  "#" // no matching route - go home
        // model,Navigation.modifyUrl (toHash model.currentPage)
    | Some (JoinChannel chanId) ->
        model, Cmd.batch
            [ Chat.Types.AppMsg.Join chanId |> ApplicationMsg |> ChatDataMsg |> Cmd.ofMsg
              Navigation.modifyUrl  "#"]
    | Some route ->
        { model with currentPage = route }, []

let init result =
    let (home, homeCmd) = Home.State.init()
    let (chinfo, chinfoCmd) = Chat.State.init()
    let (model, cmd) = urlUpdate result { currentPage = Home; home = home; chat = chinfo }
    model, Cmd.batch [ cmd
                       Cmd.map HomeMsg homeCmd 
                       Cmd.map (ChatDataMsg) chinfoCmd
                       ]

let update msg model =
    match msg with
    | HomeMsg msg ->
        let (home, homeCmd) = Home.State.update msg model.home
        { model with home = home }, Cmd.map HomeMsg homeCmd
    | ChatDataMsg msg ->
        let (chinfo, chinfoCmd) = Chat.State.update msg model.chat
        { model with chat = chinfo }, Cmd.map ChatDataMsg chinfoCmd
