module App.View

open Elmish
open Elmish.Browser.Navigation
open Elmish.Browser.UrlParser
open Fable.Core
open Fable.Core.JsInterop
open Types
open App.State
open Router
open ChatData.Types

importAll "./sass/main.sass"

open Fable.Helpers.React
open Fable.Helpers.React.Props

let menuItem label page currentPage =
    li
        [ ]
        [ a
            [ classList [ "is-active", page = currentPage ]
              Href (toHash page) ]
            [ str label ] ]

// TODO move to its own component/view
let menu (chatData: Chat) currentPage =

    let channels = chatData |> function
      | NotConnected -> 
        [
            p [] [str "connecting..."]
        ]
      | ChatData data ->
        [
          if data.Channels |> Map.isEmpty |> not then
              yield p [ClassName "menu-label"] [str "My Channels"]
              for (_, ch) in data.Channels |> Map.toSeq do
                yield menuItem ch.Name (Channel ch.Id) currentPage

          yield p [ClassName "menu-label"] [str "Join channels"]
          yield menuItem "Demo" (Channel "demo") currentPage
        ]
    
    aside
        [ ClassName "menu" ]
        [ p
            [ ClassName "menu-label" ]
            [ str "General" ]
          ul
            [ ClassName "menu-list" ]
            [ yield menuItem "Home" Home currentPage
              yield menuItem "About" Route.About currentPage
              yield hr []
              yield! channels
              ] ]

let root model dispatch =

    let pageHtml =
        function
        | Route.About -> Info.View.root
        | Home -> Home.View.root model.home (HomeMsg >> dispatch)
        | Channel chan ->
            match model.chat with
            | ChatData chatdata when chatdata.Channels |> Map.containsKey chan
              -> Channel.View.root chatdata.Channels.[chan]
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
                      [ menu model.chat model.currentPage ]
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
