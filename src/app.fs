module fschat.app

open Giraffe.HttpHandlers
open Giraffe.Middleware
open Giraffe.Razor.HttpHandlers
open Giraffe.Razor.Middleware

open fschat.Models

// ---------------------------------
// Web app
// ---------------------------------

let webApp: HttpHandler =
    choose [
        GET >=>
            choose [
                route "/" >=> razorHtmlView "Index" { Text = "Hello world, from Giraffe!" }
            ]
        setStatusCode 404 >=> text "Not Found" ]

let errorHandler (ex: System.Exception) =
    clearResponse >=> setStatusCode 500 >=> text ex.Message
