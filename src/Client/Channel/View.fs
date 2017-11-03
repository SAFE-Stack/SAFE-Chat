module Channel.View

open Fable.Helpers.React
open Fable.Helpers.React.Props

open ChatData.Types

let simpleButton txt action dispatch =
    div
        [ ClassName "column is-narrow" ]
        [ a
            [ ClassName "button"
              Style [Float "right"]
              OnClick (fun _ -> action |> dispatch) ]
            [ str txt ] ]

let root (model: ChannelData) dispatch =
    div
        [ ClassName "content" ]
            [   h1 [] [ str model.Name ]
                simpleButton "Leave" (Leave model.Id) dispatch
                p [] [str model.Topic]
                p [] [str "TBD"]
            ]