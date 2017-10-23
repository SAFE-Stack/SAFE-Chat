module App

open Suave
open Suave.OAuth
open Suave.Authentication
open Suave.Operators
open Suave.Logging
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

module Secrets =

    open System.IO
    open Suave.Utils
    open Microsoft.Extensions.Configuration

    [<Literal>]
    let CookieSecretFile = "CHAT_DATA\\COOKIE_SECRET"

    [<Literal>]
    let OAuthConfigFile = "CHAT_DATA\\suave.oauth.config"

    let readCookieSecret () =
        printfn "Reading configuration data from %s" System.Environment.CurrentDirectory
        if not (File.Exists CookieSecretFile) then
            let secret = Crypto.generateKey Crypto.KeyLength
            do (Path.GetDirectoryName CookieSecretFile) |> Directory.CreateDirectory |> ignore
            File.WriteAllBytes (CookieSecretFile, secret)

        File.ReadAllBytes(CookieSecretFile)

    // Here I'm reading my personal API keys from file stored in my %HOME% folder. You will likely define you keys in code (see below).
    let private oauthConfigData =
        if not (File.Exists OAuthConfigFile) then
            do (Path.GetDirectoryName OAuthConfigFile) |> Directory.CreateDirectory |> ignore
            File.WriteAllText (OAuthConfigFile, "{}")

        ConfigurationBuilder().SetBasePath(System.Environment.CurrentDirectory) .AddJsonFile(OAuthConfigFile).Build()

    let dump name a =
        printfn "%s: %A" name a
        a

    let oauthConfigs =
        defineProviderConfigs (fun pname c ->
            let key = pname.ToLowerInvariant()
            {c with
                client_id = oauthConfigData.[key + ":client_id"]
                client_secret = oauthConfigData.[key + ":client_secret"]}
        )
        // |> dump "oauth configs"

let startChatServer () =
    let actorSystem = ActorSystem.Create("chatapp")
    let chatServer = ChatServer.startServer actorSystem

    AppState.server <- Started (actorSystem, chatServer)

    // TODO add diag channels and actors
    ()

type UserSessionData = {
    Nickname: string
    Id: string
    UserId: Uuid
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
                yield Text "Log on via: "
                yield a "/oaquery?provider=Google" [] [Text "Google"]
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

    let loggedoff =
        page <| div [] [
            Text " You are now logged off."
        ]

let logger = Log.create "fschat"

let returnPathOrHome = 
    request (fun x -> 
        let path = 
            match (x.queryParam "returnPath") with
            | Choice1Of2 path -> path
            | _ -> "/"
        FOUND path)

let sessionStore setF = context (fun x ->
    match HttpContext.state x with
    | Some state -> setF state
    | None -> never)

let (|ParseUuid|_|) = Uuid.TryParse

let session f = 
    statefulForSession
    >=> context (fun x -> 
        match x |> HttpContext.state with
        | None -> f NoSession
        | Some state ->
            match state.get "id", state.get "nick", state.get "uid" with
            | Some id, Some nick, Some (ParseUuid userId) ->
                f (UserLoggedOn {Id = id; Nickname = nick; UserId = userId})
            | _ -> f NoSession)

let root: WebPart =
    choose [
        warbler(fun ctx ->
            let authorizeRedirectUri ="http://localhost:8083/oalogin" in   // FIXME
            // Note: logon state for current user is stored in global variable, which is ok for demo purposes.
            // in your application you shoud store such kind of data to session data
            authorize authorizeRedirectUri Secrets.oauthConfigs
                (fun loginData ->
                    // register user, obtain userid and store in session
                    let (Started (actorsystem, server)) = AppState.server
                    let userId = server |> ChatServer.registerNewUser loginData.Name [] |> Async.RunSynchronously // FIXME async

                    statefulForSession
                    >=> sessionStore (fun store ->
                            store.set "id" loginData.Id
                        >=> store.set "nick" loginData.Name
                        >=> store.set "uid" (userId.ToString())
                    )
                    >=> FOUND "/"
                )
                (fun () -> FOUND "/loggedoff")
                (fun error -> OK <| sprintf "Authorization failed because of `%s`" error.Message)
            )

        GET >=>
            session (fun session ->
                choose [
                    path "/" >=> (OK <| (View.index session |> Html.htmlToString))
                    path "/logoff" >=> (
                        deauthenticate
                        >=> (OK <| Html.htmlToString View.loggedoff)
                        )
                    pathStarts "/api" >=> 
                        match session with
                        | UserLoggedOn u ->
                            let (Started (actorSystem, server)) = AppState.server   // FIXME kinda session fn
                            choose [
                                GET >=> path "/api/channels" >=> (ChatApi.listChannels server u.UserId)
                                GET >=> pathScan "/api/channel/%s/info" (ChatApi.chanInfo server u.UserId)
                                POST >=> pathScan "/api/channel/%s/join" (ChatApi.join server u.UserId)
                                POST >=> pathScan "/api/channel/%s/leave" (ChatApi.leave server u.UserId)
                                path "/api/channel/socket" >=> (ChatApi.connectWebSocket actorSystem server u.UserId)
                            ]
                        | NoSession ->
                            BAD_REQUEST "Authorization required"
                    ]
                            
            )

        NOT_FOUND "Not Found"
    ]

let errorHandler (ex: System.Exception) =
    // FIXME clear response
    ServerErrors.INTERNAL_ERROR ex.Message
