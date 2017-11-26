module NavMenu.View

open Fable.Core.JsInterop
open Fable.Helpers.React
open Props

open Router
open Channel.Types
open Chat.Types

let private menuItem label page currentPage =
    li [] [a
            [ classList [ "is-active", page = currentPage ]
              Href (toHash page) ]
            [ str label ] ]

let private menuJoinChannelItem (ch: ChannelData) dispatch =
    li [] [ div
              [ ClassName "button has-icons-right"
                OnClick (fun _ -> dispatch (Join ch.Id))
              ]
              [ i [ClassName "fa fa-plus"] []
                str " "; str ch.Name
                span [ ClassName "icon tag is-info is-right" ] [str <| string ch.UserCount] ]
              ]

// left-side menu view
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
            [ menuItem "About" Route.About currentPage
              hr []
              p [] [str "Not connected"] ]
        ]
    | Connected (_,chat) ->
        aside
            [ ClassName "menu" ]
            [ p
                [ ClassName "menu-label" ]
                [ str "General" ]
              ul
                [ ClassName "menu-list" ]
                [ yield menuItem "About" Route.About currentPage
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
                            yield menuJoinChannelItem ch dispatch
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
