module fschat.app

open Giraffe.HttpHandlers
open Giraffe.Middleware
open Giraffe.Razor.HttpHandlers
open Giraffe.Razor.Middleware

open fschat.Models

// ---------------------------------
// Web app
// ---------------------------------

(*
    /login
    /logout
    GET /channels
    POST /channels/:chan/join
    POST /channels/:chan/leave
    POST /channels/:chan
    GET /channels/:chan/messages
*)

let webApp: HttpHandler =
    choose [
        subRoute "/channels"
            (choose [
                GET >=> route "/" >=>
                    setStatusCode 200 >=> text "TBD list of channels here"
                POST >=> routef "%s/join" (fun chan ->
                    setStatusCode 200 >=> text ("TBD Join channel " + chan))
            ])
        GET >=>
            choose [
                route "/" >=> razorHtmlView "Index" { Text = "Hello world, from Giraffe!" }
            ]
        setStatusCode 404 >=> text "Not Found" ]

let errorHandler (ex: System.Exception) =
    clearResponse >=> setStatusCode 500 >=> text ex.Message
