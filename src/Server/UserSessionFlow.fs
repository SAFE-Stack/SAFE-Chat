module UserSessionFlow

open System
open Akkling.Streams
open Akka.Streams.Dsl

open Suave
open Suave.Logging

open ChatUser
open ChannelFlow
open UserStore
open ChatServer
open SocketFlow

open FsChat
open ProtocolConv

let logger = Log.create "chatapi"

type internal IncomingMessage =
    | ChannelMessage of ChannelId * Message
    | ControlMessage of Protocol.ServerMsg
    | Trash of reason: string

// extracts message from websocket reply, only handles User input (channel * string)
let internal extractMessage message =
    try
        match message with
        | Text t ->
            match t |> Json.unjson<Protocol.ServerMsg> with
            | Protocol.UserMessage {chan = channelId; text = messageText} ->
                match Int32.TryParse channelId with
                | true, chanId -> ChannelMessage (ChannelId chanId, Message messageText)
                | _ -> Trash "Bad channel id"
            | message -> ControlMessage message                
        | x -> Trash <| sprintf "Not a Text message '%A'" x
    with e ->
        do logger.error (Message.eventX "Failed to parse message '{msg}'. Reason: {e}" >> Message.setFieldValue "msg" message  >> Message.setFieldValue "e" e)
        Trash "exception"

let create (userStore: UserStore) messageFlow controlFlow =

    let isChannelMessage = function |ChannelMessage _ -> true | _ -> false
    let extractChannelMessage (ChannelMessage (chan, message) | OtherwiseFail (chan, message)) = chan, message

    let isControlMessage = function |ControlMessage _ -> true | _ -> false
    let extractControlMessage (ControlMessage message | OtherwiseFail message) = message

    let encodeChannelMessage (getUser: GetUser) channelId : ClientMessage<UserId, Message> -> Protocol.ClientMsg Async =
        let returnUserEvent (id, ts) userid f = async {
            let! userResult = getUser userid
            let user2userInfo = function
                | Some user -> mapUserToProtocol user
                | _ -> makeBlankUserInfo "zz" "unknown"
            return Protocol.ChannelEvent {id = id; ts = ts; evt = userResult |> (user2userInfo >> f)}
        }
        function
        | ChatMessage ((id, ts), UserId authorId, Message message) ->
            async.Return <| Protocol.ChanMsg {id = id; ts = ts; text = message; chan = channelId; author = authorId}
        | Joined (idts, userid, _) ->
            returnUserEvent idts userid (fun u -> Protocol.Joined (channelId, u))
        | Left ((id, ts), UserId userid, _) ->
            async.Return <| Protocol.ChannelEvent {id = id; ts = ts; evt = Protocol.Left (channelId, userid)}
        | Updated (idts, userid) ->
            returnUserEvent idts userid (fun u -> Protocol.Updated (channelId, u))

    let userMessageFlow =
        Flow.empty<IncomingMessage, Akka.NotUsed>
        |> Flow.filter isChannelMessage
        |> Flow.map extractChannelMessage
        // |> Flow.log "User flow"
        |> Flow.viaMat messageFlow Keep.right
        |> Flow.asyncMap 1 (fun (ChannelId channelId, message) -> encodeChannelMessage userStore.GetUser (channelId.ToString()) message)

    let controlFlow =
        Flow.empty<IncomingMessage, Akka.NotUsed>
        |> Flow.filter isControlMessage
        |> Flow.map extractControlMessage
        // |> Flow.log "Control flow"
        |> Flow.via controlFlow

    let combinedFlow : Flow<IncomingMessage,Protocol.ClientMsg,_> =
        FlowImpl.split2 userMessageFlow controlFlow Keep.left

    let socketFlow =
        Flow.empty<WsMessage, Akka.NotUsed>
        |> Flow.map extractMessage
        // |> Flow.log "Extracting message"
        |> Flow.viaMat combinedFlow Keep.right
        |> Flow.map (Json.json >> Text)

    socketFlow
