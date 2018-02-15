module Info.View

open Fable.Helpers.React
open Fable.Helpers.React.Props

let root =
  div
    [ ClassName "content"; Style [ Margin "2em"] ]
    [ h1 []
        [ str "Welcome to F# Chat" ]
      h4
        [ Style [ MarginBottom "5em"] ]
        [ str "F# Chat application built with Fable, Elmish, React, Suave, Akka.Streams, Akkling" ] 
      p [] [ str "Click on the channel name to join or click '+' and type in the name of the new channel." ] 
      p [] [ str "Try the following commands in channel's input box:" ] 
      ul [] [
          li [] [b [] [str "/leave"]; str " - leaves the channel"]
          li [] [b [] [str "/join <chan name>"]; str " - joins the channel, creates if it doesn't exist"]
          li [] [b [] [str "/nick <newnick>"]; str " - changes your nickname"]
          li [] [b [] [str "/status <newstatus>"]; str " - change status"]
          li [] [b [] [str "/avatar <imageUrl>"]; str " - change user avatar"]
          ]
    ]
