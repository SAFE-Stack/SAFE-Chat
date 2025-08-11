module Logon

open Giraffe.ViewEngine

open ChatUser

type ClientSession = NoSession | UserLoggedOn of RegisteredUser

module Views =
    let private partUser (session : ClientSession) = 
        div [ _id "part-user" ] [
            match session with
            | UserLoggedOn (RegisteredUser (_, user)) ->
                p [] [ str (sprintf "Logged on as %s" user.nick) ]
                p [] []
                a [ _href "/" ] [ str "Proceed to chat screen" ]
                p [] []
                str "Or you can "
                a [ _href "/logoff" ] [ str "log off now" ]
                p [] []
            | _ ->
                form [ _method "POST" ] [
                    p [ _class "subtitle" ] [
                        str "Log on using your "
                        a [ _href "/oaquery?provider=Google" ] [ str "Google" ]
                        str " or "
                        a [ _href "/oaquery?provider=Github" ] [ str "Github" ]
                        str " account, or..."
                    ]
                    div [ _class "label" ] [
                        str "Choose a nickname"
                    ]
                    div [ _class "field" ] [
                        div [ _class "control" ] [
                            input [ _id "nickname"; _class "input"; _name "nick"; _type "text"; _required ]
                        ]
                    ]
                    div [ _class "control" ] [
                        input [ 
                            _id "login"
                            _class "button is-primary"
                            _type "submit"
                            _value "Connect anonymously"
                        ]
                    ]
                ]
        ]

    let page content =
        html [] [
            head [] [
                title [] [ str "F# Chat server" ]
                link [ _rel "stylesheet"; _href "https://cdnjs.cloudflare.com/ajax/libs/bulma/0.6.1/css/bulma.css" ]
                link [ _rel "stylesheet"; _href "logon.css" ]
            ]
            body [] [
                div [ _id "header" ] [
                    h1 [ _class "title" ] [ str "F# Chat server" ]
                    h1 [ _class "subtitle" ] [ str "Logon screen" ]
                    hr []
                ]
                content
                footer [ _class "footer" ] [
                    div [ _class "container" ] [
                        div [ _class "content has-text-centered" ] [
                            strong [] [ str "F# Chat" ]
                            str " built by "
                            a [ _href "https://github.com/OlegZee" ] [ str "Anonymous" ]
                            str " with (in alphabetical order) "
                            a [ _href "http://getakka.net" ] [ str "Akka.NET" ]
                            str ", "
                            a [ _href "https://github.com/Horusiath/Akkling" ] [ str "Akkling" ]
                            str ", "
                            a [ _href "http://fable.io" ] [ str "Fable" ]
                            str ", "
                            a [ _href "http://ionide.io" ] [ str "Ionide" ]
                            str " and "
                            a [ _href "https://giraffe.wiki" ] [ str "Giraffe" ]
                        ]
                    ]
                ]
            ]
        ]

    let index session = page (partUser session)
