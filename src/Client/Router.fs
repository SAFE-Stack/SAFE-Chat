module Router

open Elmish.Browser.UrlParser

type Route =
    | About
    | Channel of string

let route : Parser<Route->Route,Route> =
    oneOf [
        map About (s "about")
        map Channel (s "channel" </> str) ]

let toHash = function
    | About -> "#about"
    | Channel str -> "#channel/" + str
