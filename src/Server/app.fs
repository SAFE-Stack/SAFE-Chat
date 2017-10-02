module fschat.app

open Suave
open Suave.Operators
open Suave.Filters
open Suave.Redirection
open Suave.Successful
open Suave.RequestErrors

open fschat.Models

// ---------------------------------
// Web app
// ---------------------------------
module internal AppState =
// app state
    let app = { App.Channels = ["hardware"; "software"; "cats"] }
    let mutable userSession = { UserSession.UserName = "%username%"; Channels = ["cats"]}

module Pages =

    open Suave.Html

    let pageLayout pageTitle content =
        html [] [
            head [] [
                title [] pageTitle
            ]
            body [] content
        ]
    let channels chanlist =
        [
        tag "ul" [] [
            for ch in chanlist do
                yield tag "li" [] (text ch)
        ]]

let joinChannel chan =
    if not(AppState.userSession.Channels |> List.contains chan) then
        AppState.userSession <- { AppState.userSession with Channels = chan :: AppState.userSession.Channels}
    redirect "/channels"

let leaveChannel chan =
    // todo leave, redirect to channels list
    OK ("TBD leave channel " + chan)

let webApp: WebPart =
    choose [
        pathStarts "/channels" >=> choose [
            GET >=> path "/channels" >=> (Pages.channels AppState.app.Channels |> Pages.pageLayout "Channels" |> Html.renderHtmlDocument |> OK)
            GET >=> pathScan "/channels/%s/join" joinChannel
            GET >=> pathScan "/channels/%s/leave" leaveChannel
            GET >=> pathScan "/channels/%s" (fun chan ->
                 choose [
                     GET  >=> OK ("TBD view channel messages " + chan)
                     POST >=> OK ("TBD post message to " + chan)
                 ])                
        ]
        // GET >=>
        //     choose [
        //         route "/" >=> warbler (fun _ -> AppState.userSession |> razorHtmlView "Index")
        //     ]
        NOT_FOUND "Not Found"
    ]

let errorHandler (ex: System.Exception) =
    // FIXME clear response
    Suave.ServerErrors.INTERNAL_ERROR ex.Message
