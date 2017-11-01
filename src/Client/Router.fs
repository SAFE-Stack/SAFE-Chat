module Router

open Elmish.Browser.UrlParser

type Route =
    Home
    | About
    | Channel of string
    | JoinChannel of string

let route : Parser<Route->Route,Route> =
    oneOf [
        map Home (s "home")
        map About (s "about")
        map Channel (s "channel" </> str)
        map JoinChannel (s "join" </> str) ]

let toHash = function
    | About -> "#about"
    | Home -> "#home"
    | Channel str -> "#channel/" + str
    | JoinChannel str -> "#join/" + str
