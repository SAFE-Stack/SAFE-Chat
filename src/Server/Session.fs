module Session
// keeps track of channels user connected to

open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Streams

open Types
open Channel

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

type UserMessage = UserMessage of chan:Uuid * Message

/// Starts a user session flow. Session flow obtains pair of (channel * message) redirects it to respective channel, collects messages from channels and sends as output
/// Return stopSession killswitch,
/// and "subscribe" method which adds chat flow (you have to create it in advance) to session flow.
/// "Subscribe" in its turn returns "unsubscribe" killswitch.
let createUserSessionFlow (materializer: Akka.Streams.IMaterializer) =

    let inhub = BroadcastHub.Sink<UserMessage>(bufferSize = 256)
    let outhub = MergeHub.Source<Uuid * ChatClientMessage>(perProducerBufferSize = 16)

    let sourceTo (sink) (source: Source<'TOut, 'TMat>) = source.To(sink)

    let combine
            (producer: Source<UserMessage, Akka.NotUsed>)
            (consumer: Sink<Uuid * ChatClientMessage, Akka.NotUsed>)
            (chanId: Uuid) (chanFlow: Flow<Message, ChatClientMessage, _>) =

        let infilter =
            Flow.empty<UserMessage, Akka.NotUsed>
            |> Flow.filter (fun (UserMessage (chan,_)) -> chan = chanId)
            |> Flow.map (fun (UserMessage (_,message)) -> message)

        let graph =
            producer
            |> Source.viaMat (KillSwitches.Single()) Keep.right
            |> Source.via infilter
            |> Source.via chanFlow
            |> Source.map (fun message -> chanId, message)
            |> sourceTo consumer

        graph |> Graph.run materializer

    Flow.ofSinkAndSourceMat inhub combine outhub
