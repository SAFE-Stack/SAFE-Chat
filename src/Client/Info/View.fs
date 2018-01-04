module Info.View

open Fable.Helpers.React
open Fable.Helpers.React.Props

let root =
  div
    [ ClassName "content" ]
    [ h1
        [ ]
        [ str "Welcome to F# Chat" ]
      h4
        [ Style [ MarginBottom "5em"] ]
        [ str "F# Chat application built with Fable, Elmish, React, Suave, Akka.Streams, Akkling" ] 
      p
        [ ]
        [ str "Click on the channel name to join or click '+' and type in the name of the new channel." ] 
        ]
