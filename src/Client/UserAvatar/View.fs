module UserAvatar.View

open Types
open Fable.Helpers.React
open Fable.Helpers.React.Props

let root  =
  function
  | IconCssClass cls ->
      div
          [ ClassName "fs-avatar" ]
          [ i [ ClassName cls ] [] ]

  | PhotoUrl photoUrl ->
      div
          [ ClassName "fs-avatar"
            Style [BackgroundImage (sprintf "url(%s)" photoUrl) ] ]
          []

  | _ ->
      div [ ClassName "fs-avatar" ] []
