module Router

open Elmish.Browser.UrlParser

type Route =
    | About
    | Channel of string
    | JoinChannel of string

let route : Parser<Route->Route,Route> =
    oneOf [
        map About (s "about")
        map Channel (s "channel" </> str)
        map JoinChannel (s "join" </> str) ]

let toHash = function
    | About -> "#about"
    | Channel str -> "#channel/" + str
    | JoinChannel str -> "#join/" + str
