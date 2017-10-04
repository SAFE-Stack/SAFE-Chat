[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SessionStore

open System
open Akka.Actor
open Akkling
open Akkling.Streams

type ControlMsg =
    | NewSession of sessionId: string
    | StoreData of sessionId: string * key: string * data: obj
    | ReadData of sessionId: string * key: string

module internal Details =

    type UserSession = {
        userData: Map<string, obj>
        lastAccess: System.DateTime
    }

    type State = {
        sessions: Map<string, UserSession>
    }

    type ReplyMsg =
        | UserData of string * obj

    let updateData key value (s: UserSession)  =
        {s with userData = s.userData |> Map.add key value}

    let update sessionId f serverState =
        let sessionData = serverState.sessions |> Map.tryFind sessionId
        {serverState with sessions = serverState.sessions |> Map.add sessionId {f sessionData with lastAccess = DateTime.UtcNow}}

    let read sessionId key serverState =
        let sessionData = serverState.sessions |> Map.tryFind sessionId
        sessionData |> Option.bind (fun d -> d.userData |> Map.tryFind key)

open Details

type SessionStore = {holder: ControlMsg IActorRef}

/// <summary>
/// Creates session store
/// </summary>
/// <param name="system"></param>
let createStore (system: ActorSystem): SessionStore =
    let emptySession = {userData = Map.empty; lastAccess = DateTime.MinValue}

    let behavior state (ctx: Actor<_>) =
        function
        | NewSession sessionId ->
            state |> update sessionId (fun _ -> emptySession) |> ignored
        | StoreData (sessionId, key, data) ->
            state |> update sessionId (Option.defaultValue emptySession >> updateData key data) |> ignored
        | ReadData (sessionId, key) ->
            ctx.Sender() <! (state |> read sessionId key)
            ignored state
    in
    {holder =
        props <| actorOf2 (behavior {State.sessions = Map.empty}) |> (spawn system "sessions_store") |> retype
    }

let readData<'t> (sessionId: string) (key: string) (store: SessionStore): 't Option Async =
    async {
        let! value = store.holder <? ReadData (sessionId, key)
        return value |> Option.map unbox<'t>
    }

let writeData<'t> (sessionId: string) (key: string) (data: 't) (store: SessionStore): unit =
    store.holder <! StoreData (sessionId, key, box data)
