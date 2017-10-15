module fschat.app

open Suave
open Suave.Operators
open Suave.Filters
open Suave.Redirection
open Suave.Successful
open Suave.RequestErrors

open Akka.Actor
open Akkling

// ---------------------------------
// Web app
// ---------------------------------
type AppState = | Off | Started of actorSystem: ActorSystem * server: IActorRef<ChatServer.ServerControlMessage>
module internal AppState =

    let mutable server = Off
    let mutable me = Uuid.New()

let startChatServer () =
    let actorSystem = ActorSystem.Create("chatapp")
    let chatServer = ChatServer.startServer actorSystem

    AppState.server <- Started (actorSystem, chatServer)

    // TODO add testing channels and actors
    ()

let webApp: WebPart =
    choose [
        pathStarts "/api" >=> fun ctx ->
            let (Started (actorSystem, server)) = AppState.server
            choose [
                GET >=> path "/api/channels" >=> (ChatApi.listChannels server AppState.me)
                GET >=> pathScan "/api/channel/%s/info" (ChatApi.chanInfo server AppState.me)
                POST >=> pathScan "/api/channel/%s/join" (ChatApi.join server AppState.me)
                POST >=> pathScan "/api/channel/%s/leave" (ChatApi.leave server AppState.me)
                path "/api/channel/socket" >=> (ChatApi.connectWebSocket actorSystem server AppState.me)
            ] ctx
        // GET >=>
        //     choose [
        //         route "/" >=> warbler (fun _ -> AppState.userSession |> razorHtmlView "Index")
        //     ]
        NOT_FOUND "Not Found"
    ]

let errorHandler (ex: System.Exception) =
    // FIXME clear response
    Suave.ServerErrors.INTERNAL_ERROR ex.Message
