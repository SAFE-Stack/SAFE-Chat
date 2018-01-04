module NavMenu.View

open Fable.Core.JsInterop
open Fable.Helpers.React
open Props

open Router
open Channel.Types
open Chat.Types
open Fable.Import
open Fable.Import.Browser

module private Components =

  let menuItem htmlProp name topic isCurrent =
    button
      [ classList [ "btn", true; "fs-channel", true; "selected", isCurrent ]
        htmlProp ]
      [ str name
        span [] [str topic]]

  let menuItemChannel (ch: ChannelData) currentPage = 
    let targetRoute = Channel ch.Id
    let jump _ = Browser.location.hash <- toHash targetRoute
    menuItem (OnClick jump) ch.Name ch.Topic (targetRoute = currentPage)

  let menuItemChannelJoin dispatch (ch: ChannelData) = 
    let join _ = dispatch (Join ch.Id)
    menuItem (OnClick join) ch.Name ch.Topic false

open Components
open UserAvatar.Types

let menu (chatData: ChatState) currentPage dispatch =
    match chatData with
    | NotConnected ->
      [ div [] [str "not connected"] ]
    | Connected (me, chat) ->
      let opened, newChanName = chat.NewChanName |> function |Some text -> (true, text) |None -> (false, "")
      [ yield div
          [ ClassName "fs-user" ]
          [ UserAvatar.View.root (PhotoUrl "https://pbs.twimg.com/profile_images/2191150324/Avatar_Shepard_400x400.jpg")
            h3 [] [str me.Nick]
            span [] [ str "The first human Spectre"]
            button
              [ ClassName "btn"; Title "Logout" ]
              [ i [ ClassName "mdi mdi-logout-variant"] [] ]
           ]
        yield h2 []
           [ str "My Channels"
             button
               [ ClassName "btn"; Title "Create New"
                 OnClick (fun _ -> (if opened then None else Some "") |> (SetNewChanName >> dispatch)) ]
               [ i [ classList [ "mdi", true; "mdi-close", opened; "mdi-plus", not opened ] ] []]
           ]
        yield input
          [ Type "text"
            classList ["fs-new-channel", true; "open", opened]
            Placeholder "Type the channel name here..."
            Value newChanName
            AutoFocus true
            OnChange (fun ev -> !!ev.target?value |> (Some >> SetNewChanName >> dispatch) )
            OnKeyPress (fun ev -> if !!ev.which = 13 || !!ev.keyCode = 13 then dispatch CreateJoin)
            ]

        for (_, ch) in chat.Channels |> Map.toSeq do
            if ch.Joined then
                yield menuItemChannel ch currentPage

        yield h2 []
           [ str "All Channels"
             button
               [ ClassName "btn"; Title "Search" ]
               [ i [ ClassName "mdi mdi-magnify" ] []]
           ]
        for (_, ch) in chat.Channels |> Map.toSeq do
            if not ch.Joined then
                yield menuItemChannelJoin dispatch ch
      ]
