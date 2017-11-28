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

open UserSession
// ---------------------------------
// Web app
// ---------------------------------

module Secrets =

    open System.IO
    open Suave.Utils
    open Microsoft.Extensions.Configuration
    
    let (</>) a b = Path.Combine(a, b)
    let CookieSecretFile = "CHAT_DATA" </> "COOKIE_SECRET"
    let OAuthConfigFile = "CHAT_DATA" </> "suave.oauth.config"

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
    do ChatServer.createTestChannels actorSystem chatServer

    appServerState <- Some (actorSystem, chatServer)
    ()

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

let session (f: ClientSession -> WebPart) = 
    statefulForSession
    >=> context (HttpContext.state >>
        function
        | None -> f NoSession
        | Some state ->
            match state.get "nick", appServerState with
            | Some nick, Some (actorSystem, server) ->
                f (UserLoggedOn (ChatServer.UserNick nick, actorSystem, server))
            | _ -> f NoSession)

let noCache =
    Writers.setHeader "Cache-Control" "no-cache, no-store, must-revalidate"
    >=> Writers.setHeader "Pragma" "no-cache"
    >=> Writers.setHeader "Expires" "0"

let root: WebPart =
    choose [
        warbler(fun ctx ->
            // problem is that redirection leads to localhost and authorization does not go well
            let authorizeRedirectUri =
                (ctx.runtime.matchedBinding.uri "oalogin" "").ToString().Replace("127.0.0.1", "localhost")

            authorize authorizeRedirectUri Secrets.oauthConfigs
                (fun loginData ->
                    // register user, obtain userid and store in session
                    let nick, name = loginData.Name, loginData.Name // TODO lookup for user nickname in db

                    logger.info (Message.eventX "User registered by nickname {nick}"
                        >> Message.setFieldValue "nick" nick)

                    statefulForSession
                    >=> sessionStore (fun store -> store.set "nick" loginData.Name)
                    >=> FOUND "/"
                )
                (fun () -> FOUND "/logon")
                (fun error -> OK <| sprintf "Authorization failed because of `%s`" error.Message)
            )

        warbler(fun _ ->
            GET >=> path "/logon_anon" >=> ( // FIXME remove in prod builds
                let nick = "Anonymous"

                statefulForSession
                >=> sessionStore (fun store -> store.set "nick" nick)
                >=> FOUND "/"
                )
        )

        session (fun session ->
            choose [
                GET >=> path "/" >=> noCache >=> (
                    match session with
                    | NoSession -> found "/logon"
                    | _ -> Files.browseFileHome "index.html"
                    )
                GET >=> path "/logon" >=> noCache >=>
                    (OK <| (Views.index session |> Html.htmlToString))  // FIXME rename index to login
                GET >=> path "/logoff" >=> noCache >=>
                    deauthenticate >=> FOUND "/logon"

                
                path "/api/socket" >=>
                    (match session with
                    | UserLoggedOn (nick, actorSys, server) ->
                        (RestApi.connectWebSocket actorSys server nick)
                    | NoSession ->
                        BAD_REQUEST "Authorization required")

                Files.browseHome
                ]
        )

        NOT_FOUND "Not Found"
    ]
