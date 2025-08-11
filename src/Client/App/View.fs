module App.View

open Elmish
open Elmish.Navigation
open Fable.Core.JsInterop

open App.Types
open Router

open Fable.React
open Fable.React.Props
open Connection.Types

// Import SCSS for Vite
importAll "../sass/app.scss"

let root model dispatch =

    let mainAreaView = function
        | Overview -> [Overview.View.root]
        | Channel chan ->

            match model.chatPage with
            | Connected connectionInfo when connectionInfo.serverData.Channels |> Map.containsKey chan ->

                let dispatchChannelMessage m = ChatServer.Types.ChannelMsg(chan, m)
                Channel.View.root connectionInfo.serverData.Channels.[chan] (dispatchChannelMessage >> ApplicationMsg >> ChatDataMsg >> dispatch)

            | _ ->
                [div [] [str "bad channel route" ]]

    div
      [ ClassName "container" ]
      [ div
          [ ClassName "col-md-4 fs-menu" ]
          (NavMenu.View.menu model.chatPage model.currentPage (ApplicationMsg >> ChatDataMsg >> dispatch))
        div
          [ ClassName "col-xs-12 col-md-8 fs-chat" ]
          (mainAreaView model.currentPage) ]