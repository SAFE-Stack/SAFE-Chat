module Channel.View

open Fable.Core
open Fable.Helpers.React
open Fable.Helpers.React.Props

open ChatData.Types

let simpleButton txt action dispatch =
    div
        [ ClassName "column is-narrow" ]
        [ a
            [ ClassName "button"
              OnClick (fun _ -> action |> dispatch) ]
            [ str txt ] ]

let root (model: ChannelData) =
    div
        [ ClassName "content" ]
            [   h1 [] [str "Channel data"]
                p []
                    [ 
                        b [] [str model.Name]
                        p [] [str model.Topic]
                    ] 
                p [] [str "TBD"]
            ]