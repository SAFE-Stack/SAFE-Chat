module Navbar.View

open Fable.Helpers.React
open Fable.Helpers.React.Props

open UserInfo.Types

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
    | NotLoggedIn -> "Please login"
    | UserInfo u -> u.Nick
    | Error x -> "error" + x.ToString()


let navButtons userInfo =
    span
        [ ClassName "nav-item" ]
        [ div
            [ ClassName "field is-grouped" ]
            [
                p [ ClassName "control" ]
                  [ p [ClassName "userinfo"] [str <| userInfoText userInfo]]
                navButton "twitter" "https://twitter.com/OlegZee" "fa-twitter" "Twitter"
                navButton "" "/logoff" "fa-hand-o-right" "Log off"
                ] ]

let root (userInfo: UserInfo) =
    nav
        [ ClassName "nav" ]
        [ div
            [ ClassName "nav-left" ]
            [ h1
                [ ClassName "nav-item is-brand title is-4" ]
                [ str "F# Chat" ] ]
          div
            [ ClassName "nav-right" ]
            [ navButtons userInfo ] ]
