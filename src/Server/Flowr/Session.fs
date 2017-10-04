module Session

open Akka.Streams

type SessionState = {
    channels: Map<string, UniqueKillSwitch option>
    join: string -> UniqueKillSwitch Async
}

/// Connects user to a channels, initializes a new session object
let connect (channels: string list) (join: string -> UniqueKillSwitch Async) =
    async {
        let mapc chan = async {
            let! ks = join chan
            return chan, Some ks
        }
        let! joined = channels |> Seq.map mapc |> Async.Parallel
        
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