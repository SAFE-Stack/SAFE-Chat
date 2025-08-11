module App.State

open Elmish
open Elmish.Navigation
open Router
open Types

let urlUpdate (result: Option<Route>) model =
    match result with
    | None ->
        // console.error("Error parsing url")
        { model with currentPage = Overview }, Navigation.modifyUrl "#"
    | Some route ->
        { model with currentPage = route }, []

let init result =
    let connModel, connCmd = Connection.State.init()
    let model, cmd = urlUpdate result { currentPage = Overview; chatPage = connModel }
    model, Cmd.batch [
        cmd
        Cmd.map ChatDataMsg connCmd
    ]

let update msg model =
    match msg with
    | ChatDataMsg msg ->
        let (chinfo, chinfoCmd) = Connection.State.update msg model.chatPage
        { model with chatPage = chinfo }, Cmd.map ChatDataMsg chinfoCmd