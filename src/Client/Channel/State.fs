module Channel.State
open Elmish

open Types
open FsChat
open Fable.Import

let init () : ChannelData * Cmd<Msg> =
  ChannelData.Empty, Cmd.none

let update (msg: Msg) state: (ChannelData * Msg Cmd) =

    match msg with
    | SetPostText text ->
        {state with PostText = text}, Cmd.none

    | PostText ->
        match state.PostText with
        | text when String.length text > 0 ->
            let userMessage: Protocol.ChannelMsg = {
                id = 1; ts = System.DateTime.Now; text = text; chan = state.Id; author = "xxx"}
            {state with PostText = ""}, Cmd.ofMsg (Forward userMessage)
        | _ ->
            state, Cmd.none

    | Leave
    | Forward _ ->
        Browser.console.error <| sprintf "%A message is not expected in channel update." msg
        state, Cmd.none
