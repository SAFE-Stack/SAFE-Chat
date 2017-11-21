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

open Akka.Configuration
open Akka.Actor
open Akkling

// ---------------------------------
// Web app
// ---------------------------------

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

type ServerActor = IActorRef<ChatServer.ServerControlMessage>
let mutable private appServerState = None

let startChatServer () =
    let config = ConfigurationFactory.ParseString """akka {  
    stdout-loglevel = DEBUG
    loglevel = DEBUG
    // actor {                
    //     debug {  
    //           receive = on 
    //           autoreceive = on
    //           lifecycle = on
    //           event-stream = on
    //           unhandled = on
    //     }
    // }
    }  
    """
    let actorSystem = ActorSystem.Create("chatapp", config)
    let chatServer = ChatServer.startServer actorSystem

    do Diag.createDiagChannel actorSystem chatServer ("Demo", "Channel for testing purposes. Notice the bots are always ready to keep conversation.")
    // TODO move to server initialization
    chatServer <! ChatServer.ServerControlMessage.NewChannel ("Test", "test channel #1")
    chatServer <! ChatServer.ServerControlMessage.NewChannel ("Weather", "join channel to get updated")

    appServerState <- Some (actorSystem, chatServer)
    ()

module View =

    open Suave.Html

    let partUser (session : RestApi.Session) = 
        div ["id", "part-user"] [
            match session with
            | RestApi.UserLoggedOn (ChatServer.UserNick nick,_,_) ->
                yield Text (sprintf "Logged on as %s" nick)
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
        match x.queryParam "returnPath" with
        | Choice1Of2 path -> path
        | _ -> "/"
        |> FOUND)

let sessionStore setF = context (fun x ->
    match HttpContext.state x with
    | Some state -> setF state
    | None -> never)

let (|ParseUuid|_|) = Uuid.TryParse

let session (f: RestApi.Session -> WebPart) = 
    statefulForSession
    >=> context (HttpContext.state >>
        function
        | None -> f RestApi.NoSession
        | Some state ->
            match state.get "nick", appServerState with
            | Some nick, Some (actorSystem, server) ->
                f (RestApi.UserLoggedOn (ChatServer.UserNick nick, actorSystem, server))
            | _ -> f RestApi.NoSession)

let root: WebPart =
    choose [
        warbler(fun _ ->
            let authorizeRedirectUri ="http://localhost:8083/oalogin" in   // FIXME hardcoded path
            authorize authorizeRedirectUri Secrets.oauthConfigs
                (fun loginData ->
                    // register user, obtain userid and store in session
                    let (Some (actorsystem, server)) = appServerState
                    let nick, name = loginData.Name, loginData.Name // TODO lookup for user nickname in db
                    do server |> ChatServer.registerNewUser (ChatServer.UserNick nick) name (Some loginData.Id) [] |> Async.RunSynchronously // FIXME async
                    logger.info (Message.eventX "User registered by nickname {nick}"
                        >> Message.setFieldValue "nick" nick)

                    statefulForSession
                    >=> sessionStore (fun store -> store.set "nick" loginData.Name)
                    >=> FOUND "/"
                )
                (fun () -> FOUND "/loggedoff")
                (fun error -> OK <| sprintf "Authorization failed because of `%s`" error.Message)
            )

        warbler(fun _ ->
            GET >=> path "/logonfast" >=> ( // FIXME remove in prod builds
                let externalId, nick, name = "11111112222222333333", "Joe", "Joe Smith"


                let (Some (actorsystem, server)) = appServerState

                do server |> ChatServer.registerNewUser (ChatServer.UserNick nick) name (Some externalId) [] |> Async.RunSynchronously

                statefulForSession
                >=> sessionStore (fun store -> store.set "nick" nick)
                >=> FOUND "/"
                )
        )

        session (fun session ->
            choose [
                GET >=> path "/" >=> (
                    match session with
                    | RestApi.NoSession -> found "/logonfast"
                    | _ -> Files.browseFileHome "index.html"
                    )
                GET >=> path "/logon" >=>
                    (OK <| (View.index session |> Html.htmlToString))  // FIXME rename index to login
                GET >=> path "/logoff" >=>
                    deauthenticate
                    >=> (OK <| Html.htmlToString View.loggedoff)

                RestApi.api session

                Files.browseHome                            
                ]
        )

        NOT_FOUND "Not Found"
    ]

let errorHandler (ex: System.Exception) =
    // FIXME clear response
    ServerErrors.INTERNAL_ERROR ex.Message
