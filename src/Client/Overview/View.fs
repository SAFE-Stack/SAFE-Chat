module Overview.View

open Fable.Helpers.React
open Fable.Helpers.React.Props

let root =
  div [] []

  (*
  div
    [ ClassName "content"; Style [ Margin "2em"] ]
    [ h1 []
        [ str "Welcome to F# Chat" ]
      h4
        [ Style [ MarginBottom "5em"] ]
        [ str "Here goes overview screen..." ] 
      p [] [ str "TBD" ] 
      p [] [ str "Channels:" ] 
      ul [] [
          li [] [b [] [str "Channel 1"]; str " - channel desctiption"]
          ]
    ]
*)