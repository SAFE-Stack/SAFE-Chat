module Suave.OAuth

open System.Net.Http
open Newtonsoft.Json

open Suave
open Suave.Cookie
open Suave.Operators
open Suave.Filters
open Suave.Logging

type DataEnc = | FormEncode | JsonEncode | Plain

type ProviderConfig = {
    authorize_uri: string;
    exchange_token_uri: string;
    client_id: string;
    client_secret: string
    request_info_uri: string
    scopes: string
    token_response_type: DataEnc
    customize_req: HttpRequestMessage -> unit
}

exception private OAuthException of string

/// <summary>
/// Result type for successive login callback.
/// </summary>
type LoginData = {ProviderName: string; Id: string; Name: string; AccessToken: string; ProviderData: Map<string,obj>}
type FailureData = {Code: int; Message: string; Info: obj}

let EmptyConfig =
    {authorize_uri = ""; exchange_token_uri = ""; request_info_uri = ""; client_id = ""; client_secret = "";
    scopes = ""; token_response_type = FormEncode; customize_req = ignore}

/// <summary>
/// Default (incomplete) oauth provider settings.
/// </summary>
let private providerConfigs =
    Map.empty
    |> Map.add "google"
        {EmptyConfig with
            authorize_uri = "https://accounts.google.com/o/oauth2/auth"
            exchange_token_uri = "https://www.googleapis.com/oauth2/v3/token"
            request_info_uri = "https://www.googleapis.com/oauth2/v1/userinfo"
            scopes = "profile"
            token_response_type = JsonEncode
            }
    |> Map.add "github"
        {EmptyConfig with
            authorize_uri = "https://github.com/login/oauth/authorize"
            exchange_token_uri = "https://github.com/login/oauth/access_token"
            request_info_uri = "https://api.github.com/user"
            scopes = ""}
    |> Map.add "facebook"
        {EmptyConfig with
            authorize_uri = "https://www.facebook.com/dialog/oauth"
            exchange_token_uri = "https://graph.facebook.com/oauth/access_token"
            request_info_uri = "https://graph.facebook.com/me"
            scopes = ""}

/// <summary>
/// Allows to completely define provider configs.
/// </summary>
/// <param name="f"></param>
let defineProviderConfigs f = providerConfigs |> Map.map f

module internal util =

    open System.Text
    open System.Net

    let urlEncode = WebUtility.UrlEncode:string->string
    let asciiEncode = Encoding.ASCII.GetBytes:string -> byte[]

    let formEncode = List.map (fun (k,v) -> String.concat "=" [k; urlEncode v]) >> String.concat "&"

    let formDecode (s:System.String) =
        s.Split('&') |> Seq.map(fun p -> let [|a;b|] = p.Split('=') in a,b) |> Map.ofSeq

    let parseJsObj js =
    //     let jss = new JavaScriptSerializer()
    //     jss.DeserializeObject(js) :?> seq<_> |> Seq.map (|KeyValue|) |> Map.ofSeq
        //Json.fromJson
        JsonConvert.DeserializeObject<Map<string, obj>> js

module private impl =

    let logger = Log.create "Suave.OAuth"

    let httpClient = new HttpClient()
    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Suave App") |> ignore

    let getConfig ctx (configs: Map<string,ProviderConfig>) =

        let providerKey =
            match ctx.request.queryParam "provider" with
            |Choice1Of2 code -> code.ToLowerInvariant()
            | _ -> "google"

        match configs.TryFind providerKey with
        | None -> raise (OAuthException "bad provider key in query")
        | Some c -> providerKey, c


    let login (configs: Map<string,ProviderConfig>) redirectUri (fnSuccess: LoginData -> WebPart) : WebPart =

        // TODO use Uri to properly add parameter to redirectUri

        (fun ctx ->
            let providerKey,config = configs |> getConfig ctx

            let extractToken =
                match config.token_response_type with
                | JsonEncode -> util.parseJsObj >> Map.tryFind "access_token" >> Option.bind (unbox<string> >> Some)
                | FormEncode -> util.formDecode >> Map.tryFind "access_token" >> Option.bind (unbox<string> >> Some)
                | Plain ->      Some

            logger.debug (Message.eventX "Param access code {code}"
                >> Message.setFieldValue "code" (ctx.request.queryParam "code")
            )
            
            let code =
                match ctx.request.queryParam "code" with
                | Choice2Of2 _ ->    raise (OAuthException "server did not return access code")
                | Choice1Of2 code -> code

            let parms = [
                "code", code
                "client_id", config.client_id
                "client_secret", config.client_secret
                "redirect_uri", redirectUri + "?provider=" + providerKey
                "grant_type", "authorization_code"
            ]

            async {
                use authRequest = new HttpRequestMessage(HttpMethod.Post, config.exchange_token_uri)
                authRequest.Content <- new StringContent(parms |> util.formEncode, System.Text.Encoding.ASCII, "application/x-www-form-urlencoded")
                do config.customize_req authRequest

                let! response = httpClient.SendAsync authRequest |> Async.AwaitTask
                let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                logger.debug (Message.eventX "Exchange token response code is {code}. Body:\"{body}\""
                    >> Message.setFieldValue "code" response.StatusCode
                    >> Message.setFieldValue "body" responseBody
                )

                response.EnsureSuccessStatusCode() |> ignore
                let accessToken = 
                    responseBody |> extractToken |> function
                    | None -> raise (OAuthException "failed to extract access token")
                    | Some token -> token

                let uri = config.request_info_uri + "?" + (["access_token", accessToken] |> util.formEncode)
                use request = new HttpRequestMessage(HttpMethod.Get, uri)
                do config.customize_req request

                let! response = httpClient.SendAsync request |> Async.AwaitTask
                response.EnsureSuccessStatusCode() |> ignore

                let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                let userInfo: Map<string,obj> = responseBody |> util.parseJsObj

                logger.debug (Message.eventX "user_info response: {user_info}"
                    >> Message.setFieldValue "user_info" userInfo
                )

                let userId = userInfo.["id"] |> System.Convert.ToString
                let userName = userInfo.["name"] |> System.Convert.ToString

                return! fnSuccess
                    { ProviderName = providerKey;
                      Id = userId; Name = userName; AccessToken = accessToken;
                      ProviderData = userInfo} ctx
            }
        )

/// <summary>
/// Login action handler (low-level API).
/// </summary>
/// <param name="configs"></param>
/// <param name="redirectUri"></param>
let redirectAuthQuery (configs:Map<string,ProviderConfig>) redirectUri : WebPart =
    warbler (fun ctx ->

        let providerKey, config = configs |> impl.getConfig ctx

        let parms = [
            "redirect_uri", redirectUri + "?provider=" + providerKey
            "response_type", "code"
            "client_id", config.client_id
            "scope", config.scopes
            ]

        let q = config.authorize_uri + "?" + (parms |> util.formEncode)

        impl.logger.debug (Message.eventX "Sending redirect request: {request}"
            >> Message.setFieldValue "request" q
        )

        Redirection.FOUND q
    )

/// <summary>
/// OAuth login provider handler (low-level API).
/// </summary>
/// <param name="configs"></param>
/// <param name="redirectUri"></param>
/// <param name="f_success"></param>
/// <param name="f_failure"></param>
let processLogin (configs: Map<string,ProviderConfig>) redirectUri (fnSuccess: LoginData -> WebPart) (fnFailure: FailureData -> WebPart) : WebPart =

    fun ctx ->
        async {
            try return! impl.login configs redirectUri fnSuccess ctx
            with
                | OAuthException e -> return! fnFailure {FailureData.Code = 1; Message = e; Info = e} ctx
                | e -> return! fnFailure {FailureData.Code = 1; Message = e.Message; Info = e} ctx
        }

/// <summary>
/// Gets the login callback URL by an HTTP request.
/// Provided for demo purposes as it cannot handle more complicated scenarios involving proxy, docker, azure etc.
/// </summary>
/// <param name="ctx"></param>
let buildLoginUrl (ctx:HttpContext) =
    let buildr = System.UriBuilder ctx.request.url
    buildr.Host <- ctx.request.host
    buildr.Path <- "oalogin"
    buildr.Query <- ""

    buildr.ToString()

/// <summary>
/// Handles OAuth authorization requests. Stores auth info in session cookie
/// </summary>
/// <param name="loginRedirectUri">Provide external URL to /oalogin handler.</param>
/// <param name="configs">OAuth configs</param>
/// <param name="fnLogin">Store login data and provide continuation webpart</param>
/// <param name="fnLogout">Handle logout request and provide continuation webpart</param>
/// <param name="fnFailure"></param>
let authorize loginRedirectUri configs fnLogin fnLogout fnFailure =
    choose [
        path "/oaquery" >=> GET >=> context(fun _ -> redirectAuthQuery configs loginRedirectUri)

        path "/logout" >=> GET >=> 
            context(fun _ ->
                let cont = fnLogout()
                unsetPair Authentication.SessionAuthCookie >=> unsetPair State.CookieStateStore.StateCookie >=> cont
            )
        path "/oalogin" >=> GET >=>
            context(fun _ ->
                processLogin configs loginRedirectUri
                    (fnLogin >> (fun wpb -> Authentication.authenticated Session false >=> wpb))
                    fnFailure
                )
    ]

/// <summary>
/// Utility handler which verifies if current user is authenticated using session cookie.
/// </summary>
/// <param name="configs"></param>
/// <param name="protectedApi"></param>
/// <param name="fnFailure"></param>
let protectedPart protectedApi fnFailure:WebPart =
    Authentication.authenticate Session false
        (fun () -> Choice2Of2 fnFailure)
        (fun _ ->  Choice2Of2 fnFailure)
        protectedApi
