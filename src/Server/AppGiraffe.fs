module AppGiraffe

open System
open System.IO
open System.Net
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.FileProviders
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open System.Security.Claims

open Giraffe
open Giraffe.ViewEngine

open Akka.Configuration
open Akka.Actor
open Akkling.Streams

open ChatTypes
open ChatUser
open ChatServer
open Logon
open SocketFlow
open UserSessionFlow

// ---------------------------------
// Configuration and State
// ---------------------------------

let private (</>) a b = Path.Combine(a, b)

module Secrets =
    let CookieSecretFile = "CHAT_DATA" </> "COOKIE_SECRET"
    let OAuthConfigFile = "CHAT_DATA" </> "oauth.config"

    let readCookieSecret () =
        printfn "Reading configuration data from %s" System.Environment.CurrentDirectory
        if not (File.Exists CookieSecretFile) then
            let secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
            do (Path.GetDirectoryName CookieSecretFile) |> Directory.CreateDirectory |> ignore
            File.WriteAllBytes (CookieSecretFile, secret)
        File.ReadAllBytes(CookieSecretFile)

    let oauthConfigData =
        if not (File.Exists OAuthConfigFile) then
            do (Path.GetDirectoryName OAuthConfigFile) |> Directory.CreateDirectory |> ignore
            File.WriteAllText (OAuthConfigFile, """{
      "google": {
      	"client_id": "<type in client id string>",
      	"client_secret": "<type in client secret>"
      	}
}"""    )
        ConfigurationBuilder().SetBasePath(System.Environment.CurrentDirectory).AddJsonFile(OAuthConfigFile).Build()

type AppState = {
    ActorSystem: ActorSystem option
    UserStore: UserStore.UserStore option  
    ChatServer: ChatServer.ServerT option
}

let mutable private appServerState: AppState = { ActorSystem = None; UserStore = None; ChatServer = None }

// ---------------------------------
// Chat Server Initialization
// ---------------------------------

let startChatServer () = async {
    try
        printfn "Initializing actor system with in-memory persistence..."
        
        let configStr = """akka {  
    stdout-loglevel = WARNING
    loglevel = DEBUG
    persistence {
        journal {
            plugin = "akka.persistence.journal.inmem"
            inmem {
                class = "Akka.Persistence.Journal.MemoryJournal, Akka.Persistence"
            }
        }
        snapshot-store {
            plugin = "akka.persistence.snapshot-store.inmem"
            inmem {
                class = "Akka.Persistence.Snapshot.MemorySnapshotStore, Akka.Persistence"
            }
        }
    }
    actor {
        ask-timeout = 30s
        creation-timeout = 30s
        serializers {
            json = "Akka.Serialization.NewtonSoftJsonSerializer"
        }
        serialization-bindings {
            "System.Object" = json
        }
        debug {
            unhandled = on
            lifecycle = on
        }
    }
}"""
        let config = ConfigurationFactory.ParseString(configStr)

        printfn "Creating actor system..."
        let actorSystem = ActorSystem.Create("chatapp", config)
        
        printfn "Creating user store..."
        let userStore = UserStore.UserStore actorSystem

        // Wait for actor system to initialize (shorter wait for in-memory)
        printfn "Waiting for actor system initialization..."
        do! Async.Sleep(2000)

        printfn "Starting chat server..."
        let chatServer = ChatServer.startServer actorSystem
        
        // Give the chat server time to start (shorter wait for in-memory)
        printfn "Waiting for chat server to start..."
        do! Async.Sleep(1000)
        
        // Try to initialize channels with retry logic
        let rec tryInitializeChannels retryCount =
            async {
                try
                    printfn "Creating diagnostic channel (attempt %d)..." (6 - retryCount)
                    do! Diag.createDiagChannel userStore.GetUser actorSystem chatServer (UserStore.UserIds.echo, "Demo", "Channel for testing purposes. Notice the bots are always ready to keep conversation.")

                    printfn "Creating default channels (attempt %d)..." (6 - retryCount)
                    do! chatServer |> getOrCreateChannel "Test" "empty channel" (GroupChatChannel { autoRemove = false }) |> Async.Ignore
                    do! chatServer |> getOrCreateChannel "About" "interactive help" (OtherChannel <| AboutChannelActor.props UserStore.UserIds.system) |> Async.Ignore
                    
                    printfn "Channels created successfully."
                with
                | ex when retryCount > 0 ->
                    printfn "Channel creation failed (attempt %d): %s. Retrying..." (6 - retryCount) ex.Message
                    do! Async.Sleep(3000)
                    return! tryInitializeChannels (retryCount - 1)
                | ex ->
                    printfn "Channel creation failed after all retries: %s" ex.Message
                    return failwith (sprintf "Failed to create channels: %s" ex.Message)
            }
        
        do! tryInitializeChannels 5

        printfn "Chat server initialization completed successfully."
        
        appServerState <- { 
            ActorSystem = Some actorSystem
            UserStore = Some userStore 
            ChatServer = Some chatServer 
        }
        return ()
    with
    | ex -> 
        printfn "Error during chat server initialization: %s" ex.Message
        printfn "Stack trace: %s" ex.StackTrace
        return failwith (sprintf "Failed to initialize chat server: %s" ex.Message)
}

// ---------------------------------
// Session Management
// ---------------------------------

let getUserFromSession (ctx: HttpContext) = async {
    match appServerState.UserStore with
    | Some userStore ->
        let userId = ctx.Session.GetString("userid")
        if not (String.IsNullOrEmpty userId) then
            let! result = userStore.GetUser (UserId userId)
            return result |> Option.map (fun user -> RegisteredUser (UserId userId, user))
        else
            return None
    | None -> return None
}

// ---------------------------------
// OAuth Helpers
// ---------------------------------

let getUserImageUrl (claims: seq<Claim>) : string option =
    let claimsMap = claims |> Seq.map (fun c -> c.Type, c.Value) |> Map.ofSeq
    let getClaim claim = claimsMap |> Map.tryFind claim
    
    None
    |> Option.orElseWith (fun () -> getClaim "avatar_url")
    |> Option.orElseWith (fun () -> getClaim "picture")

// ---------------------------------
// WebSocket Handler
// ---------------------------------

let websocketHandler : HttpFunc -> HttpFunc =
    fun next ctx -> task {
        let! userOpt = getUserFromSession ctx
        match userOpt, appServerState.ActorSystem, appServerState.UserStore, appServerState.ChatServer with
        | Some user, Some actorSystem, Some userStore, Some server ->
            if ctx.WebSockets.IsWebSocketRequest then
                let! webSocket = ctx.WebSockets.AcceptWebSocketAsync()
                let session = UserSession.Session(server, userStore, user)
                let materializer = actorSystem.Materializer()
                
                let messageFlow = createMessageFlow materializer
                let socketFlow = createSessionFlow userStore messageFlow session.ControlFlow
                
                let materialize materializer source sink =
                    session.SetListenChannel(
                        source
                        |> Source.viaMat socketFlow Keep.right
                        |> Source.toMat sink Keep.left
                        |> Graph.run materializer |> Some)
                    ()
                
                do! handleWebsocketMessages actorSystem materialize webSocket ctx.RequestAborted
                return! next ctx
            else
                ctx.Response.StatusCode <- 400
                return! text "WebSocket connection required" next ctx
        | _ ->
            ctx.Response.StatusCode <- 401
            return! text "Authorization required" next ctx
    }

// ---------------------------------
// HTTP Handlers
// ---------------------------------

let indexHandler : HttpFunc -> HttpFunc =
    fun next ctx -> task {
        let! userOpt = getUserFromSession ctx
        match userOpt with
        | Some _ -> 
            let clientPublicPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "..", "..", "..", "..", "Client", "public", "index.html")
            let indexPath = Path.GetFullPath clientPublicPath
            if File.Exists indexPath then
                return! htmlFile indexPath next ctx
            else
                return! text "Chat client not found. Please build the client project." next ctx
        | None -> return! redirectTo false "/logon" next ctx
    }

let logonGetHandler : HttpFunc -> HttpFunc =
    fun next ctx -> task {
        let! userOpt = getUserFromSession ctx
        let session = 
            match userOpt with
            | Some user -> UserLoggedOn user
            | None -> NoSession
        let html = Logon.Views.index session |> RenderView.AsString.htmlDocument
        return! htmlString html next ctx
    }

let logonPostHandler : HttpFunc -> HttpFunc =
    fun next ctx -> task {
        match appServerState.UserStore with
        | Some userStore ->
            let! body = ctx.ReadBodyFromRequestAsync()
            let nick = body.Substring(5) |> WebUtility.UrlDecode |> WebUtility.HtmlDecode
            let user = {ChatUser.makeNew (Anonymous nick) nick with imageUrl = makeUserImageUrl "monsterid" nick}
            let! registerResult = userStore.Register user
            match registerResult with
            | Ok (RegisteredUser(UserId userid, _)) ->
                ctx.Session.SetString("userid", userid)
                return! redirectTo false "/" next ctx
            | Result.Error message ->
                return! text (sprintf "Register failed because of `%s`" message) next ctx
        | None ->
            return! text "Server not initialized" next ctx
    }

let logoffHandler : HttpFunc -> HttpFunc =
    fun next ctx -> task {
        let! userOpt = getUserFromSession ctx
        match userOpt, appServerState.UserStore with
        | Some (RegisteredUser (userId, _)), Some userStore ->
            userStore.Unregister userId
            ctx.Session.Clear()
            return! redirectTo false "/logon" next ctx
        | _ ->
            return! redirectTo false "/logon" next ctx
    }

let oauthCallbackHandler (provider: string) : HttpFunc -> HttpFunc =
    fun next ctx -> task {
        match appServerState.UserStore with
        | Some userStore ->
            try
                let! result = ctx.AuthenticateAsync(provider)
                if result.Succeeded then
                    let claims = result.Principal.Claims
                    let name = claims |> Seq.tryFind (fun c -> c.Type = ClaimTypes.Name) |> Option.map (fun c -> c.Value) |> Option.defaultValue "Unknown"
                    let id = claims |> Seq.tryFind (fun c -> c.Type = ClaimTypes.NameIdentifier) |> Option.map (fun c -> c.Value) |> Option.defaultValue (Guid.NewGuid().ToString())
                    
                    let imageUrl = getUserImageUrl claims |> Option.orElseWith (fun () -> makeUserImageUrl "wavatar" name)
                    let identity = Person {oauthId = Some id; email = None; name = None}
                    let user = {ChatUser.makeNew identity name with imageUrl = imageUrl}
                    
                    let! registerResult = userStore.Register user
                    match registerResult with
                    | Ok (RegisteredUser(UserId userid, _)) ->
                        ctx.Session.SetString("userid", userid)
                        return! redirectTo false "/" next ctx
                    | Result.Error message ->
                        return! text (sprintf "Register failed because of `%s`" message) next ctx
                else
                    return! text "OAuth authentication failed" next ctx
            with
            | ex -> return! text (sprintf "OAuth error: %s" ex.Message) next ctx
        | None ->
            return! text "Server not initialized" next ctx
    }

// ---------------------------------
// Web Application
// ---------------------------------

let webApp : HttpFunc -> HttpFunc =
    choose [
        GET >=> choose [
            route "/" >=> indexHandler
            route "/logon" >=> logonGetHandler
            route "/logoff" >=> logoffHandler
            route "/health" >=> text "OK"
            route "/api/socket" >=> websocketHandler
            routef "/oauth/callback/%s" oauthCallbackHandler
        ]
        POST >=> choose [
            route "/logon" >=> logonPostHandler
        ]
        RequestErrors.NOT_FOUND "Not Found"
    ]

// ---------------------------------
// Application Configuration
// ---------------------------------

let configureServices (services: IServiceCollection) =
    // Add session support
    services.AddDistributedMemoryCache() |> ignore
    services.AddSession(fun options ->
        options.IdleTimeout <- TimeSpan.FromHours(24.0)
        options.Cookie.HttpOnly <- true
        options.Cookie.IsEssential <- true
    ) |> ignore
    
    // Add authentication
    services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie() |> ignore
    
    // Add OAuth providers (if configured)
    try
        let config = Secrets.oauthConfigData
        let googleClientId = config.["google:client_id"]
        let googleClientSecret = config.["google:client_secret"]
        
        if not (String.IsNullOrEmpty googleClientId) && not (String.IsNullOrEmpty googleClientSecret) then
            services.AddAuthentication()
                .AddGoogle(fun options ->
                    options.ClientId <- googleClientId
                    options.ClientSecret <- googleClientSecret
                    options.CallbackPath <- "/oauth/callback/google"
                ) |> ignore
    with
    | _ -> printfn "OAuth configuration not found or invalid"
    
    services.AddGiraffe() |> ignore

let configureApp (app: IApplicationBuilder) =
    app.UseSession() |> ignore
    app.UseAuthentication() |> ignore
    app.UseWebSockets() |> ignore
    
    // Configure static files to serve from client public directory
    let clientPublicPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "..", "..", "..", "..", "Client", "public")
    let staticPath = Path.GetFullPath(clientPublicPath)
    if Directory.Exists(staticPath) then
        app.UseStaticFiles(new StaticFileOptions(FileProvider = new PhysicalFileProvider(staticPath))) |> ignore
    
    app.UseGiraffe(webApp)

[<EntryPoint>]
let main argv =
    async {
        try
            // Start the chat server (Akka.NET backend)
            do! startChatServer()
            
            // Parse command line arguments
            let port = 
                if argv.Length > 0 && argv.[0].StartsWith("--port=") then
                    match Int32.TryParse(argv.[0].Substring(7)) with
                    | (true, p) -> p
                    | _ -> 8083
                else 8083

            // Create and configure web application
            let builder = WebApplication.CreateBuilder(argv)
            
            // Configure services
            configureServices builder.Services
            builder.Services.AddLogging(fun logging -> 
                logging.AddConsole() |> ignore
                logging.SetMinimumLevel(LogLevel.Information) |> ignore) |> ignore

            // Build the application
            let app = builder.Build()
            
            // Configure request pipeline  
            configureApp app
            
            // Configure URLs
            app.Urls.Add($"http://localhost:{port}")
            
            printfn "Starting F# Chat server with Giraffe"
            printfn $"Server listening on http://localhost:{port}"
            printfn "Press Ctrl+C to shutdown"
            
            // Run the application
            do! app.RunAsync() |> Async.AwaitTask
            
            return 0
        with
        | ex ->
            printfn "Fatal error during application startup: %s" ex.Message
            printfn "Stack trace: %s" ex.StackTrace
            return 1
    }
    |> Async.RunSynchronously