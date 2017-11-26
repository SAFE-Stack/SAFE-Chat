module App.View

open Elmish
open Elmish.Browser.Navigation
open Elmish.Browser.UrlParser
open Fable.Websockets.Elmish
open Fable.Core.JsInterop

open Types
open App.State
open Router
open Chat.Types

open Fable.Helpers.React
open Props

importAll "./sass/main.sass"

let root model dispatch =

    let pageHtml = function
        | Route.About -> Info.View.root
        | Channel chan ->
            match model.chat with
            | Connected (_,chatdata) when chatdata.Channels |> Map.containsKey chan
              -> Channel.View.root chatdata.Channels.[chan]
                  ((fun m -> ChannelMsg (chan, m)) >> ApplicationMsg >> ChatDataMsg >> dispatch)
            | _ -> div [] [str "bad channel route" ]

    div
      []
      [ div
          [ ClassName "navbar-bg" ]
          [ div
              [ ClassName "container" ]
              [ Navbar.View.root model.chat ] ]
        div
          [ ClassName "section" ]
          [ div
              [ ClassName "container" ]
              [ div
                  [ ClassName "columns" ]
                  [ div
                      [ ClassName "column is-3" ]
                      [ NavMenu.View.menu model.chat model.currentPage (ApplicationMsg >> ChatDataMsg >> dispatch)]
                    div
                      [ ClassName "column" ]
                      [ pageHtml model.currentPage ] ] ] ] ]

open Elmish.React
open Elmish.Debug
open Elmish.HMR

// App
Program.mkProgram init update root
|> Program.toNavigable (parseHash Router.route) urlUpdate
#if DEBUG
|> Program.withDebugger
|> Program.withHMR
#endif
|> Program.withReact "elmish-app"
|> Program.run
