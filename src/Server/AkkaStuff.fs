module AkkaStuff

open System
open Newtonsoft.Json.Linq

type EventAdapter(__ : Akka.Actor.ExtendedActorSystem) =

    interface Akka.Persistence.Journal.IEventAdapter with

        member __.Manifest(_ : obj) = 
            let manifestType = typeof<Newtonsoft.Json.Linq.JObject>
            sprintf "%s,%s" manifestType.FullName <| manifestType.Assembly.GetName().Name

        member __.ToJournal(evt : obj) : obj = 
            new JObject(
                new JProperty("evtype", evt.GetType().FullName),
                new JProperty("value", JObject.FromObject(evt))
            )
            :> obj

        member __.FromJournal(evt : obj, _ : string) : Akka.Persistence.Journal.IEventSequence =
            match evt with
            | :? JObject as jobj ->
                match jobj.TryGetValue("evtype") with
                    | false, _ -> box jobj
                    | _, typ ->
                        let t = Type.GetType(typ.ToString())
                        jobj.["value"].ToObject(t)
                |> Akka.Persistence.Journal.EventSequence.Single

            | _ ->
                Akka.Persistence.Journal.EventSequence.Empty