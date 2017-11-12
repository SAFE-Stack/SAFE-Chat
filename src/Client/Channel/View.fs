module Channel.View

open Fable.Core.JsInterop
open Fable.Helpers.React
open Props

open Types

let private divCtl ctl = div [ClassName "control"] [ctl]

let simpleButton txt action dispatch =
    div
        [ ClassName "column is-narrow" ]
        [ a
            [ ClassName "button"
              Style [Float "right"]
              OnClick (fun _ -> action |> dispatch) ]
            [ str txt ] ]

let chanMessages (messages: Message list) =
    div
      []
      [ for m in messages ->
          p [] [str m.Text]
      ]

let postMessage model dispatch =
  div
    [ ClassName "field has-addons" ]            
    [ divCtl <|
        input
          [ ClassName "input"
            Type "text"
            Placeholder "Type the message here"
            Value model.PostText
            AutoFocus true
            OnChange (fun ev -> !!ev.target?value |> (SetPostText >> dispatch))
            ]
      divCtl <|
        button
         [ ClassName "button is-primary" 
           OnClick (fun _ -> PostText |> dispatch)]
         [str "Post"]
    ]

let root (model: ChannelData) dispatch =
    div
      [ ClassName "content" ]
        [   h1 [] [ str model.Name ]
            simpleButton "Leave" Leave dispatch
            p [] [str model.Topic]
            postMessage model dispatch
            chanMessages model.Messages
        ]