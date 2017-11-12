module App.View

open Elmish
open Elmish.Browser.Navigation
open Elmish.Browser.UrlParser
open Fable.Websockets.Elmish
open Fable.Core.JsInterop

open Types
open App.State
open Router
open Channel.Types
open Chat.Types

importAll "./sass/main.sass"

open Fable.Helpers.React
open Props

let menuItem label page currentPage =
    li [] [a
            [ classList [ "is-active", page = currentPage ]
              Href (toHash page) ]
            [ str label ] ]

let menuJoinChannelItem (ch: ChannelData) =
    li [] [a
            [ Href (JoinChannel ch.Id |> toHash) ]
            [ div [ClassName "control has-icons-right"]
                [ i [ClassName "fa fa-plus"] []
                  str " "; str ch.Name
                  span [ ClassName "icon tag is-info is-right" ] [str <| string ch.UserCount] ]
                ]
            ]

// TODO move to its own component/view
let menu (chatData: ChatState) currentPage dispatch =

    let divCtl ctl = div [ClassName "control"] [ctl]
    
    match chatData with
    | NotConnected ->
      aside
        [ ClassName "menu" ]
        [ p
            [ ClassName "menu-label" ]
            [ str "General" ]
          ul
            [ ClassName "menu-list" ]
            [ menuItem "Home" Home currentPage
              menuItem "About" Route.About currentPage
              hr []
              p [] [str "connecting..."] ]
        ]
    | Connected (_,chat) ->
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
                  if chat.Channels |> Map.exists(fun _ v -> v.Joined) then
                    yield p [ClassName "menu-label"] [str "My Channels"]
                    for (_, ch) in chat.Channels |> Map.toSeq do
                        if ch.Joined then
                            yield menuItem ch.Name (Channel ch.Id) currentPage

                  if chat.Channels |> Map.exists(fun _ v -> not v.Joined) then
                    yield p [ClassName "menu-label"] [str "Join channels"]
                    for (_, ch) in chat.Channels |> Map.toSeq do
                        if not ch.Joined then
                            yield menuJoinChannelItem ch                  
                  ]
              p [ClassName "menu-label"] [str "... or create you own"]
              div
                [ ClassName "field has-addons" ]            
                [ divCtl <|
                    input
                      [ ClassName "input"
                        Type "text"
                        Placeholder "Type the channel name"
                        Value chat.NewChanName
                        AutoFocus true
                        OnChange (fun ev -> !!ev.target?value |> SetNewChanName |> dispatch )
                        ]
                  divCtl <|
                    button
                     [ ClassName "button is-primary" 
                       OnClick (fun _ -> CreateJoin |> dispatch)]
                     [str "Join"]
                ]
            ]

let root model dispatch =

    let pageHtml =
        function
        | Route.About -> Info.View.root
        | Home -> Home.View.root model.home (HomeMsg >> dispatch)
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
                      [ menu model.chat model.currentPage (ApplicationMsg >> ChatDataMsg >> dispatch)]
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
