module UserSession

open System

open Akkling
open Akkling.Streams
open Akka.Streams
open Akka.Streams.Dsl

open Suave.Logging

open ChatUser
open ChatTypes
open UserStore
open ChatServer

open FsChat
open ProtocolConv

type ChannelList = ChannelList of Map<ChannelId, UniqueKillSwitch>

let private logger = Log.create "usersession"

module private Implementation =
    let byChanId cid c = (c:ChannelData).cid = cid

    // Creates a Flow instance for user in channel.
    // When materialized flow connects user to channel and starts bidirectional communication.
    let createChannelFlow (channelActor: IActorRef<ChannelMessage>) user =
        let chatInSink = Sink.toActorRef (ChannelCommand (ParticipantLeft user)) channelActor

        let fin =
            (Flow.empty<_, Akka.NotUsed>
                |> Flow.map (fun msg -> PostMessage(user, msg) |> ChannelCommand)
            ).To(chatInSink)

        // The counter-part which is a source that will create a target ActorRef per
        // materialization where the chatActor will send its messages to.
        // This source will only buffer one element and will fail if the client doesn't read
        // messages fast enough.
        let notifyNew sub = channelActor <! ChannelCommand (NewParticipant (user, sub)); Akka.NotUsed.Instance
        let fout = Source.actorRef OverflowStrategy.DropHead 100 |> Source.mapMaterializedValue notifyNew

        Flow.ofSinkAndSource fin fout

    let join serverChannelResult listenChannel (ChannelList channels) meUserId =
        match serverChannelResult, listenChannel with
        | Ok (chan: ChannelData), Some listen ->
            let ks = listen chan.cid (createChannelFlow chan.channelActor meUserId)
            Ok (channels |> Map.add chan.cid ks |> ChannelList, chan)
        | Result.Error err, _ ->
            Result.Error ("getChannel failed: " + err)
        | _, None ->
            Result.Error "listenChannel is not set"

    let leave (ChannelList channels) chanId =
        match Map.tryFind chanId channels with
        | Some killswitch ->
            do killswitch.Shutdown()
            Ok (channels |> (Map.remove chanId >> ChannelList), ())
        | None ->
            Result.Error "User is not joined channel"

    let leaveAll (ChannelList channels) =
        do channels |> Map.iter (fun _ killswitch -> killswitch.Shutdown())
        Ok (ChannelList Map.empty, ())

    let replyErrorProtocol requestId errtext =
        Protocol.ClientMsg.CmdResponse (requestId, Protocol.CannotProcess errtext |> Protocol.Error)

    let reply requestId = function
        | Ok response ->    response
        | Result.Error e -> replyErrorProtocol requestId e

    let (|CommandPrefix|_|) (p:string) (s:string) =
        if s.StartsWith(p, StringComparison.OrdinalIgnoreCase) then
            Some(s.Substring(p.Length).TrimStart())
        else
            None

open Implementation
open AsyncUtil

type Session(server, userStore: UserStore, userArg: RegisteredUser) =

    let (RegisteredUser (meUserId, _)) = userArg
    let mutable (RegisteredUser (_, meUser)) = userArg

    // session data
    let mutable channels = ChannelList Map.empty
    let mutable listenChannel = None

    let hasJoined channelId =
        let (ChannelList channelsMap) = channels
        channelsMap |> Map.containsKey channelId

    let updateChannels requestId f = function
        | Ok (newChannels, response) -> channels <- newChannels; f response
        | Result.Error e ->            replyErrorProtocol requestId e

    let notifyChannels message = async {
        do logger.debug (Message.eventX "notifyChannels")
        let (ChannelList channelList) = channels
        let! serverChannels = server |> (listChannels (fun {cid = chid} -> Map.containsKey chid channelList))
        match serverChannels with
        | Ok list ->
            do logger.debug (Message.eventX "notifyChannels: {list}" >> Message.setFieldValue "list" list)
            list |> List.iter(fun chan -> chan.channelActor <! message)
        | _ ->
            do logger.error (Message.eventX "notifyChannel: Failed to get channel list")
            ()
        return ()
    }

    let updateUser requestId fn = function
        | str when System.String.IsNullOrWhiteSpace str ->
            async.Return <| replyErrorProtocol requestId "Invalid (blank) value is not allowed"
        | newValue -> async {
            let meNew = meUser |> fn newValue
            let! updateResult = userStore.Update (meUserId, meNew) // TODO add user id as separate field
            match updateResult with
            | Ok (RegisteredUser (_, updatedUser)) ->
                meUser <- updatedUser
                do! notifyChannels (ChannelCommand (ParticipantUpdate meUserId))
                return Protocol.CmdResponse (requestId, Protocol.UserUpdated (mapUserToProtocol <| RegisteredUser (meUserId, meUser)))
            | Result.Error e ->
                return replyErrorProtocol requestId e
        }

    let rec processControlCommand requestId command = async {
        let replyJoinedChannel requestId chaninfo =
            chaninfo |> updateChannels requestId (fun ch -> Protocol.CmdResponse (requestId, Protocol.CommandResponse.JoinedChannel ch))
        match command with
        | Protocol.Join (IsChannelId channelId) when hasJoined channelId ->
            return replyErrorProtocol requestId "User already joined channel"

        | Protocol.Join (IsChannelId channelId) ->
            let! serverChannel = getChannel (byChanId channelId) server
            let result = join serverChannel listenChannel channels meUserId
            let chaninfo = result |> Result.map(fun (l,chan) -> l, mapChanInfo chan)
            return replyJoinedChannel requestId chaninfo

        | Protocol.Join _ ->
            return replyErrorProtocol requestId "bad channel id"

        | Protocol.JoinOrCreate channelName ->
            // user channels are all created with autoRemove, system channels are not
            let! channelResult = server |> getOrCreateChannel channelName "" (GroupChatChannel { autoRemove = true })
            match channelResult with
            | Ok channelId when hasJoined channelId ->
                return replyErrorProtocol requestId "User already joined channel"

            | Ok channelId ->
                let! serverChannel = getChannel (byChanId channelId) server
                let result = join serverChannel listenChannel channels meUserId
                let chaninfo = result |> Result.map(fun (l,chan) -> l, mapChanInfo chan)

                do userStore.UpdateUserJoinedChannel (meUserId, channelId)
                return replyJoinedChannel requestId chaninfo
            | Result.Error err ->
                return replyErrorProtocol requestId err

        | Protocol.Leave chanIdStr ->
            return chanIdStr |> function
                | IsChannelId channelId ->
                    let result = leave channels channelId
                    do userStore.UpdateUserLeftChannel (meUserId, channelId)
                    result |> updateChannels requestId (fun _ -> Protocol.CmdResponse (requestId, Protocol.LeftChannel chanIdStr))
                | _ ->
                    replyErrorProtocol requestId "bad channel id"
        | Protocol.Ping ->
            return Protocol.CmdResponse (requestId, Protocol.Pong)

        | Protocol.UserCommand {command = text; chan = chanIdStr } ->
            let optionOfStr s =
                if System.String.IsNullOrWhiteSpace s then None else Some s

            match text with
            | CommandPrefix "/leave" _ ->
                return! processControlCommand requestId (Protocol.Leave chanIdStr)
            | CommandPrefix "/join" chanName ->
                return! processControlCommand requestId (Protocol.JoinOrCreate chanName)
            | CommandPrefix "/nick" newNick ->
                let update nick u = {u with nick = nick }
                return! updateUser requestId update newNick
            | CommandPrefix "/status" newStatus ->
                let update status u = {u with UserInfo.status = optionOfStr status }
                return! updateUser requestId update newStatus
            | CommandPrefix "/avatar" newAvatarUrl ->
                let update ava u = {u with imageUrl = optionOfStr ava }: UserInfo
                return! updateUser requestId update newAvatarUrl
            | _ ->
                return replyErrorProtocol requestId "command was not processed"
    }

    let processControlMessage = function
        | Protocol.ServerMsg.Greets ->
            let createChannelFlows =
                let createFlow listen { cid = chanid; channelActor = actor } =
                    chanid, listen chanid (createChannelFlow actor meUserId)
                function
                | Some listen -> List.map(createFlow listen) >> Map.ofList >> ChannelList
                | _ -> fun _  -> ChannelList Map.empty

            listChannels (fun _ -> true) server
            |> AsyncResult.bind (fun channelsData ->
                async {
                    let channelList = channelsData |> List.map mapChanInfo
                    let joinedChannels = channelsData |> List.filter(fun c -> meUser.channelList |> List.contains c.cid)
                    // restore connected channels
                    channels <- joinedChannels |> createChannelFlows listenChannel

                    return Protocol.ClientMsg.Hello {
                            me = mapUserToProtocol <| RegisteredUser(meUserId, meUser)
                            channels = channelList }
                        |> Result.Ok
                })
            |> Async.map (reply "")

        | Protocol.ServerMsg.ServerCommand (requestId, command) ->
            processControlCommand requestId command

        | _ ->
            async.Return <| replyErrorProtocol "-" "event was not processed"

    let controlMessageFlow = Flow.empty<_, Akka.NotUsed> |> Flow.asyncMap 1 processControlMessage

    let serverEventsSource: Source<Protocol.ClientMsg, Akka.NotUsed> =
        let notifyNew sub = startSession server meUserId sub; Akka.NotUsed.Instance
        let source = Source.actorRef OverflowStrategy.Fail 1 |> Source.mapMaterializedValue notifyNew

        source |> Source.map (
            let m x = Protocol.ServerEvent { id = 0; ts = DateTime.Now; evt = x}
            function
            | AddChannel ch -> ch |> (mapChanInfo >> Protocol.NewChannel >> m)
            | DropChannel ch -> ch |> (mapChanInfo >> Protocol.RemoveChannel >> m)
        )

    let controlFlow =
        Flow.empty<Protocol.ServerMsg, Akka.NotUsed>
        |> Flow.via controlMessageFlow
        |> Flow.mergeMat serverEventsSource Keep.left

    with
        member __.ControlFlow = controlFlow
        member __.SetListenChannel(lsn) = listenChannel <- lsn
