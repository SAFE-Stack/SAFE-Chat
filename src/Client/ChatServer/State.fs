module ChatServer.State

open Browser.Dom
open Elmish
open Elmish.Navigation

open FsChat
open Types

type private ChannelMsg = Channel.Types.Msg
type private ChannelModel = Channel.Types.Model

module private Conversions =

    let mapUserInfo isMe (u: Protocol.ChanUserInfo) : Channel.Types.UserInfo =
        { Id = u.id; Nick = u.nick; IsBot = u.isbot
          Status = u.status
          Online = true; ImageUrl = Core.Option.ofObj u.imageUrl
          isMe = isMe u.id}

    let mapChannel (ch: Protocol.ChannelInfo) : Channel.Types.ChannelInfo =
        {Id = ch.id; Name = ch.name; Topic = ch.topic; UserCount = ch.userCount}

    let mapUserMessage (msg: Protocol.ChannelMessageInfo): Protocol.UserId * string Channel.Types.Envelope =
        (msg.author, {Id = msg.id; Ts = msg.ts; Content = msg.text})

module private Implementation =

    let mutable private lastRequestId = 10000
    let toCommand x =
        let reqId = lastRequestId.ToString()
        lastRequestId <- lastRequestId + 1
        
        Some <| Protocol.ServerMsg.ServerCommand (reqId, x)

    let updateChannel chanId (f: ChannelModel -> ChannelModel * ChannelMsg Cmd) (chat: Model) : Model * Msg Cmd=
        match chat.Channels |> Map.tryFind chanId with
        | Some channel ->
            match f channel with
            | newData, cmd when newData = channel && cmd.IsEmpty -> chat, Cmd.none
            | newData, cmd ->
                { chat with Channels = chat.Channels |> Map.add chanId newData },
                  cmd |> Cmd.map (fun x -> ChatServer.Types.ChannelMsg (chanId, x))
        | None ->
            console.error ("Channel %s update failed - channel not found", chanId)
            chat, Cmd.none

    let updateChannelData isMe channel (chanData: Protocol.ActiveChannelData) chat =
        let users = chanData.users |> List.map (Conversions.mapUserInfo isMe)
        let messages = chanData.lastMessages |> List.sortBy(fun msg -> msg.id) |> List.map Conversions.mapUserMessage
        
        let chanData, cmd =
            fst (Channel.State.init()) |> Channel.State.update (Channel.Types.Init (channel, users, messages))
        { chat with Channels = chat.Channels |> Map.add channel.Id chanData}, cmd

open Implementation

let init (hello: Protocol.HelloInfo) =

    let me = Conversions.mapUserInfo ((=) hello.me.id) hello.me
    let channels = hello.channels |> List.map (fun ch -> ch.id, Conversions.mapChannel ch) |> Map.ofList
    in
    { ChannelList = channels; Me = me; Channels = Map.empty; NewChanName = None }, Cmd.none

// Processes server message
let private processServerMessage (msg: Protocol.ClientMsg) (state: Model) : Model * Cmd<Msg> =

    let isMe = (=) state.Me.Id

    let mapCmd f (state, cmd) = state, cmd |> Cmd.map f
    let ignoreCmd (state, _) = state, Cmd.none

    let joinChannel channel (chat: ChatServer.Types.Model) =
        let chanData, cmd =
            Channel.State.init() |> fst
            |> Channel.State.update (Channel.Types.Init (channel, [], []))
        {chat with Channels = chat.Channels |> Map.add channel.Id chanData}, cmd

    match msg with
    | Protocol.CmdResponse (reqId, reply) ->
        match reply with
        | Protocol.UserUpdated newUser ->
            { state with Me = Conversions.mapUserInfo isMe newUser }, Cmd.none

        | Protocol.CommandResponse.JoinedChannel chanInfo ->
            let newServerData, cmd = state |> joinChannel (Conversions.mapChannel chanInfo)
            in
            newServerData, Cmd.batch [
                  cmd |> Cmd.map (fun msg -> ChatServer.Types.ChannelMsg (chanInfo.id, msg))
                  Router.Channel chanInfo.id |> Router.toHash |> Navigation.newUrl ]

        | Protocol.LeftChannel channelId ->
            state.Channels |> Map.tryFind channelId
            |> function
            | Some _ ->
                { state with Channels = state.Channels |> Map.remove channelId},
                    Router.Overview |> Router.toHash |> Navigation.newUrl
            | _ ->
                console.error ("Channel not found", channelId)
                state, Cmd.none

        | Protocol.Pong ->
            console.debug ("Pong", reqId)
            state, Cmd.none

        | Protocol.Error error ->
            console.error ("Server replied with error", error)    // FIXME report error to user
            state, Cmd.none

    | Protocol.ClientMsg.ChanMsg msg ->
        let channelCmd = msg |> Conversions.mapUserMessage |> Channel.Types.AppendUserMessage
        state |> updateChannel msg.chan (Channel.State.update channelCmd)

    | Protocol.ClientMsg.ServerEvent { evt = Protocol.ChannelEvent (chan, evt) } ->
        let userInfo user = Conversions.mapUserInfo isMe user

        let chan, message =
            evt |> function
            | Protocol.Joined user  -> chan, Channel.Types.UserJoined (userInfo user)
            | Protocol.Left userid  -> chan, Channel.Types.UserLeft userid
            | Protocol.Updated user -> chan, Channel.Types.UserUpdated (userInfo user)

        updateChannel chan (Channel.State.update message) state |> ignoreCmd

    | Protocol.ClientMsg.ServerEvent { evt = Protocol.NewChannel chan } ->
        { state with ChannelList = state.ChannelList |> Map.add chan.id (Conversions.mapChannel chan)}, Cmd.none

    | Protocol.ClientMsg.ServerEvent { evt = Protocol.RemoveChannel chan } ->
        { state with ChannelList = state.ChannelList |> Map.remove chan.id }, Cmd.none

    | Protocol.ClientMsg.ServerEvent { evt = Protocol.JoinedChannel chanData } ->
        let chanInfo = state.ChannelList.[chanData.channelId]
        in
        updateChannelData isMe chanInfo chanData state |> mapCmd (fun msg -> ChannelMsg (chanData.channelId, msg))

    | unknown ->
        console.error ("Unexpected message in Connected state, ignoring ", unknown)
        state, Cmd.none

// Processes application (internal) message
let update (msg: Msg) (state: Model) :(Model * Msg Cmd * Protocol.ServerMsg option) =

    match msg with
    | Nop -> state, Cmd.none, None

    | ChannelMsg (chanId, ChannelMsg.Forward text) ->
        let message =
            match text with
            | cmd when cmd.StartsWith "/" -> Protocol.UserCommand {command = cmd; chan = chanId} |> toCommand
            | _ -> Some <| Protocol.UserMessage {text = text; chan = chanId}

        state, Cmd.none, message

    | ChannelMsg (chanId, ChannelMsg.Leave) ->
        state, Cmd.none, Protocol.Leave chanId |> toCommand

    | ChannelMsg (chanId, msg) ->
        let newState, cmd = state |> updateChannel chanId (Channel.State.update msg)
        newState, cmd, None

    | SetNewChanName name ->
        { state with NewChanName = name }, Cmd.none, None
        
    | CreateJoin ->
        match state.NewChanName with
        | Some channelName ->
            state, Cmd.ofMsg <| (SetNewChanName None), Protocol.JoinOrCreate channelName |> toCommand
        | None -> state, Cmd.none, None
    | Join chanId ->
        state, Cmd.none, Protocol.Join chanId |> toCommand
    | Leave chanId ->
        state, Cmd.none, Protocol.Leave chanId |> toCommand

    | ServerMessage message ->
        let state, cmd = processServerMessage message state
        state, cmd, None