module Views

open UserSession
open Suave.Html

let partUser (session : ClientSession) = 
    div ["id", "part-user"] [
        match session with
        | UserLoggedOn (ChatServer.UserNick nick,_,_) ->
            yield Text (sprintf "Logged on as %s" nick)
            yield a "/logoff" [] [Text "Log off"]
        | _ ->
            yield p [] [
                Text "Log on via: "
                a "/oaquery?provider=Google" [] [Text "Google"] ]
            yield p [] [
                Text "... or continue "
                a "/logon_anon" [] [Text "Anonymously"] ]
    ]

let page content =
    html [] [
        head [] [
            title [] "F# Chat server"
        ]

        body [] [
            div ["id", "header"] [
                tag "h1" [] [
                    a "/" [] [Text "F# Chat server"]
                ]
            ]
            content

            div ["id", "footer"] [
                Text "built with (in alphabetical order) "
                a "http://getakka.net" [] [Text "Akka.NET"]
                Text ", "
                a "https://github.com/Horusiath/Akkling" [] [Text "Akkling"]
                Text ", "
                a "http://fable.io" [] [Text "Fable"]
                Text " and "
                a "http://suave.io" [] [Text "Suave.IO"]
            ]
        ]
    ]

let index session = page (partUser session)

let loggedoff =
    page <| div [] [
        Text " You are now logged off."
    ]
