module Channel.State
open Elmish

open Types
open Fable.Import

let init () : ChannelData * Cmd<Msg> =
  ChannelData.Empty, Cmd.none

let update (msg: Msg) state: (ChannelData * Msg Cmd) =

    match msg with
    | SetPostText text ->
        {state with PostText = text}, Cmd.none

    | PostText ->
        match state.PostText with
        | text when text.Trim() <> "" ->
            {state with PostText = ""}, Cmd.ofMsg (Forward text)
        | _ ->
            state, Cmd.none

    | Leave
    | Forward _ ->
        Browser.console.error <| sprintf "%A message is not expected in channel update." msg
        state, Cmd.none
