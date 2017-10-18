module App

open Suave
open Suave.Authentication
open Suave.Operators
open Suave.Filters
open Suave.Redirection
open Suave.Successful
open Suave.RequestErrors
open Suave.State.CookieStateStore

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

    // TODO add diag channels and actors
    ()

type UserSessionData = {
    Nickname: string
    Id: string
}

type Session = NoSession | UserLoggedOn of UserSessionData

module View =

    open Suave.Html

    let partUser (session : Session) = 
        div ["id", "part-user"] [
            match session with
            | UserLoggedOn session -> 
                yield Text (sprintf "Logged on as %s, " session.Nickname)
                yield a "/logoff" [] [Text "Log off"]
            | _ ->
                yield a "/logon" [] [Text "Log on"]
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

    let logon = page <| div [] [
        Text "Click Doogle to login "
        a "/loggedon" [] [Text "Doogle"]
    ]

let returnPathOrHome = 
    request (fun x -> 
        let path = 
            match (x.queryParam "returnPath") with
            | Choice1Of2 path -> path
            | _ -> "/"
        Redirection.FOUND path)

let sessionStore setF = context (fun x ->
    match HttpContext.state x with
    | Some state -> setF state
    | None -> never)

let session f = 
    statefulForSession
    >=> context (fun x -> 
        match x |> HttpContext.state with
        | None -> f NoSession
        | Some state ->
            match state.get "id", state.get "nick" with
            | Some id, Some nick -> 
                f (UserLoggedOn {Id = id; Nickname = nick})
            | _ -> f NoSession)
            
let root: WebPart =
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
        GET >=>
            session (fun session ->
                choose [
                    path "/" >=> (OK <| (View.index session |> Html.htmlToString))
                    path "/logon" >=> (OK <| Html.htmlToString View.logon)
                    path "/loggedon" >=> (
                        authenticated Cookie.CookieLife.Session false
                        >=> statefulForSession
                        >=> sessionStore (fun store ->
                            store.set "id" "12312312312312"
                            >=>
                            store.set "nick" "alibaba"
                        )
                        >=> returnPathOrHome
                    )
                ]
            )
        NOT_FOUND "Not Found"
    ]

let errorHandler (ex: System.Exception) =
    // FIXME clear response
    Suave.ServerErrors.INTERNAL_ERROR ex.Message
