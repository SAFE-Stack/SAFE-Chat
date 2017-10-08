module Session
// keeps track of channels user connected to

open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Streams

type SessionState = {
    channels: Map<Uuid, UniqueKillSwitch option>
    join: Uuid -> UniqueKillSwitch Async
}

/// Connects user to a channels, initializes a new session object
let connect (channels: Uuid list) (join: Uuid -> UniqueKillSwitch Async) =
    async {
        let joinc chan = async {
            let! ks = join chan
            return chan, Some ks
        }
        let! joined = channels |> Seq.map joinc |> Async.Parallel
        
        return { SessionState.channels = joined |> Map.ofSeq; join = join }
    }

/// Disconnects from all channels
let disconnect (session: SessionState) =
    session.channels |> Map.iter (fun _ -> function | Some ks -> ks.Shutdown() | _ -> ())
    { session with channels = Map.empty }

/// Joins channel
let join (session: SessionState) chan =
    async {
        let! killswitch = session.join chan
        return {session with channels = session.channels |> Map.add chan (Some killswitch)}
    }

/// Leaves channel
let leaveChannel (session: SessionState) chan =
    session.channels
        |> Map.tryFind chan |> function
        | Some (Some killSwitch) -> killSwitch.Shutdown()
        | _ -> ()
    {session with channels = session.channels |> Map.remove chan}

/// Gets channel list the user is joined to
let channelList (state: SessionState) =
    state.channels |> Map.toList |> List.map fst
