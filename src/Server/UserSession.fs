module UserSession

open System

open Akkling
open Akkling.Streams
open Akka.Streams
open Akka.Streams.Dsl

open ChatUser
open ChannelFlow
open ChatServer

open FsChat
open ProtocolConv

type ChannelList = ChannelList of Map<ChannelId, UniqueKillSwitch>

module private Implementation =
    let byChanId cid c = (c:ChannelData).id = cid

    let join serverChannelResult listenChannel (ChannelList channels) meUserId =
        match serverChannelResult, listenChannel with
        | Ok chan, Some listen ->
            let ks = listen chan.id (createChannelFlow chan.channelActor meUserId)
            Ok (channels |> Map.add chan.id ks |> ChannelList, chan)
        | Error err, _ ->
            Error ("getChannel failed: " + err)
        | _, None ->
            Error "listenChannel is not set"

    let leave (ChannelList channels) chanId =
        match Map.tryFind chanId channels with
        | Some killswitch ->
            do killswitch.Shutdown()
            Ok (channels |> (Map.remove chanId >> ChannelList), ())
        | None ->
            Error "User is not joined channel"

    let leaveAll (ChannelList channels) =
        do channels |> Map.iter (fun _ killswitch -> killswitch.Shutdown())
        Ok (ChannelList Map.empty, ())

    let replyErrorProtocol requestId errtext =
        Protocol.CannotProcess (requestId, errtext) |> Protocol.ClientMsg.Error

    let reply requestId = function
        | Ok response ->    response
        | Result.Error e -> replyErrorProtocol requestId e

    let (|CommandPrefix|_|) (p:string) (s:string) =
        if s.StartsWith(p, StringComparison.OrdinalIgnoreCase) then
            Some(s.Substring(p.Length).TrimStart())
        else
            None

    let isMember (ChannelList channels) channelId = channels |> Map.containsKey channelId

open Implementation
type Session(server, me) =

    let meUserId = getUserId me
    // session data
    let mutable channels = ChannelList Map.empty
    let mutable listenChannel = None

    let updateChannels requestId f = function
        | Ok (newChannels, response) -> channels <- newChannels; f response
        | Result.Error e ->            replyErrorProtocol requestId e

    let makeChannelInfoResult v = async {
        match v with
        | Ok (arg1, channel: ChannelData) ->
            let! (userIds: UserId list) = channel.channelActor <? ListUsers
            let! users = UserStore.getUsers userIds
            let chaninfo = { mapChanInfo channel with users = users |> List.map mapUserToProtocol}
            return Ok (arg1, chaninfo)
        | Result.Error e -> return Result.Error e
    }

    let rec processControlMessage message =
        async {
            let requestId = "" // TODO take from server message
            let replyJoinedChannel chaninfo =
                chaninfo |> updateChannels requestId (fun ch -> Protocol.JoinedChannel {ch with joined = true})
            match message with

            | Protocol.ServerMsg.Greets ->
                let makeChanInfo chanData =
                    { mapChanInfo chanData with joined = isMember channels chanData.id}

                let makeHello channels =
                    Protocol.ClientMsg.Hello {me = mapUserToProtocol me; channels = channels |> List.map makeChanInfo}

                let! serverChannels = server |> (listChannels (fun _ -> true))
                return serverChannels |> (Result.map makeHello >> reply "")

            | Protocol.ServerMsg.Join (IsChannelId channelId) when isMember channels channelId ->
                return replyErrorProtocol requestId "User already joined channel"

            | Protocol.ServerMsg.Join (IsChannelId channelId) ->
                let! serverChannel = getChannel (byChanId channelId) server
                let result = join serverChannel listenChannel channels meUserId
                let! chaninfo = makeChannelInfoResult result
                return replyJoinedChannel chaninfo

            | Protocol.ServerMsg.Join _ ->
                return replyErrorProtocol requestId "bad channel id"

            | Protocol.ServerMsg.JoinOrCreate channelName ->
                let! channelResult = server |> getOrCreateChannel channelName
                match channelResult with
                | Ok channelData when isMember channels channelData.id ->
                    return replyErrorProtocol requestId "User already joined channel"

                | Ok channelData ->
                    let! serverChannel = getChannel (byChanId channelData.id) server
                    let result = join serverChannel listenChannel channels meUserId
                    let! chaninfo = makeChannelInfoResult result
                    return replyJoinedChannel chaninfo
                | Result.Error err ->
                    return replyErrorProtocol requestId err

            | Protocol.ServerMsg.Leave chanIdStr ->
                return chanIdStr |> function
                    | IsChannelId channelId ->
                        let result = leave channels channelId
                        result |> updateChannels requestId (fun _ -> Protocol.LeftChannel chanIdStr)
                    | _ ->
                        replyErrorProtocol requestId "bad channel id"

            | Protocol.ServerMsg.ControlCommand {text = text; chan = chanIdStr } ->
                match text.ToLower() with
                | CommandPrefix "/leave" _ ->
                    return! processControlMessage (Protocol.ServerMsg.Leave chanIdStr)
                | CommandPrefix "/join" chanName ->
                    return! processControlMessage (Protocol.ServerMsg.JoinOrCreate chanName)
                | _ ->
                    return replyErrorProtocol requestId "command was not processed"

            | _ ->
                return replyErrorProtocol requestId "event was not processed"
        }

    let controlMessageFlow = Flow.empty<_, Akka.NotUsed> |> Flow.asyncMap 1 processControlMessage

    let serverEventsSource: Source<Protocol.ClientMsg, Akka.NotUsed> =
        let notifyNew sub = startSession server (ChatUser.getUserId me) sub; Akka.NotUsed.Instance
        let source = Source.actorRef OverflowStrategy.Fail 1 |> Source.mapMaterializedValue notifyNew

        source |> Source.map (function
            | AddChannel ch -> ch |> (mapChanInfo >> Protocol.ClientMsg.NewChannel)
            | DropChannel ch -> ch |> (mapChanInfo >> Protocol.ClientMsg.RemoveChannel)
        )

    let controlFlow =
        Flow.empty<Protocol.ServerMsg, Akka.NotUsed>
        |> Flow.via controlMessageFlow
        |> Flow.mergeMat serverEventsSource Keep.left

    with
        member __.ControlFlow = controlFlow
        member __.SetListenChannel(lsn) = listenChannel <- lsn
