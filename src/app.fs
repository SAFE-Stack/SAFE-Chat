module fschat.app

open Giraffe.HttpHandlers
open Giraffe.Middleware
open Giraffe.Razor.HttpHandlers
open Giraffe.Razor.Middleware

open fschat.Models

// ---------------------------------
// Web app
// ---------------------------------
module internal AppState =
// app state
    let app = { App.Channels = ["hardware"; "software"; "cats"] }
    let mutable userSession = { UserSession.UserName = "%username%"; Channels = ["cats"]}

module Pages =

    open Giraffe.XmlViewEngine

    let pageLayout pageTitle content =
        html [] [
            head [] [
                title [] [encodedText pageTitle]
            ]
            body [] content
        ]

let joinChannel chan =
    if not(AppState.userSession.Channels |> List.contains chan) then
        AppState.userSession <- { AppState.userSession with Channels = chan :: AppState.userSession.Channels}
    redirectTo false "/"

let leaveChannel chan =
    // todo leave, redirect to channels list
    setStatusCode 200 >=> text ("TBD leave channel " + chan)

let webApp: HttpHandler =
    choose [
        subRoute "/channels"
            (choose [
                GET >=> route "" >=> razorHtmlView "Channels" AppState.app
                GET >=> routef "/%s/join" joinChannel
                GET >=> routef "/%s/leave" leaveChannel
                routef "/%s" (fun chan ->
                    choose [
                        GET  >=> setStatusCode 200 >=> text ("TBD view channel messages " + chan)
                        POST >=> setStatusCode 200 >=> text ("TBD post message to " + chan)
                    ])                
            ])
        GET >=>
            choose [
                route "/" >=> warbler (fun _ -> AppState.userSession |> razorHtmlView "Index")
            ]
        setStatusCode 404 >=> text "Not Found" ]

let errorHandler (ex: System.Exception) =
    clearResponse >=> setStatusCode 500 >=> text ex.Message
