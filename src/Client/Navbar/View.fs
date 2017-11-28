module Navbar.View

open Fable.Helpers.React
open Props

open Chat.Types

let navButton classy href faClass txt =
    p
        [ ClassName "control" ]
        [ a
            [ ClassName (sprintf "button %s" classy)
              Href href ]
            [ span
                [ ClassName "icon" ]
                [ i
                    [ ClassName (sprintf "fa %s" faClass) ]
                    [ ] ]
              span
                [ ]
                [ str txt ] ] ]

let private userInfoText =
    function
    | NotConnected -> "Please login"
    | Connected (me, _) -> me.Nick

let navButtons chat =
    span
        [ ClassName "navbar-item" ]
        [ div
            [ ClassName "field is-grouped" ]
            [
                p [ ClassName "control" ]
                  [ p [ClassName "userinfo"] [str <| userInfoText chat]]
                navButton "" "/logoff" "fa-hand-o-right" "Log off"
                ] ]

let root (chat: ChatState) =
    nav
        [ ClassName "navbar" ]
        [ div
            [ ClassName "navbar-brand" ]
            [ h1
                [ ClassName "nav-item title" ]
                [ str "F# Chat" ] ]
          div
            [ ClassName "navbar-menu"
              Id "navbarMenu" ]
            [ 
              div
                [ ClassName "navbar-end" ]
                [ navButtons chat ] ]
            ]
