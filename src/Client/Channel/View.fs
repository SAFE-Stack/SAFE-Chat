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

let chanMessages (users: Map<string, UserInfo>) (messages: Message list) =

    let content (m: Message) =
      let user =
        users |> Map.tryFind m.AuthorId
        |> function | Some u -> u | _ -> {Nick = m.AuthorId; IsBot = false; Online = false}

      [ strong [] [str user.Nick]; str " "; small [] [str "31m"]
        br []
        str m.Text ]

    div
      []
      [ for m in messages ->
          div
            [ ClassName ""]
            [ article
                [ ClassName "media"]
                [ div
                    [ ClassName "media-left"]
                    [ figure
                        [ ClassName "image is-48x48"]
                        [ img [Src "https://bulma.io/images/placeholders/128x128.png"; Alt "Image"] ] ]
                  div
                    [ ClassName "media-content"]
                    [ div
                        [ ClassName "content"] [ p [] (content m) ]
                      nav
                        [ ClassName "level is-mobile"]
                        [ div
                            [ ClassName "level-left"]
                            [ for cls in [] -> // "fa-reply"; "fa-retweet"; "fa-heart"] ->
                                a
                                  [ ClassName "level-item"]
                                  [ span
                                      [ ClassName "icon is-small" ]
                                      [ i [ ClassName <| "fa " + cls] [] ] ] ]
                            ]
                        ]
                  hr []
                ]
            ]
      ]

let postMessage model dispatch =
  div
    [ ClassName "field has-addons postmessage" ]            
    [ divCtl <|
        input
          [ ClassName "input"
            Type "text"
            Placeholder "Type the message here"
            Value model.PostText
            AutoFocus true
            OnChange (fun ev -> !!ev.target?value |> (SetPostText >> dispatch))
            OnKeyPress (fun ev -> if !!ev.which = 13 || !!ev.keyCode = 13 then dispatch PostText)
            ]
      divCtl <|
        button
         [ ClassName "button is-primary" 
           OnClick (fun _ -> PostText |> dispatch)]
         [str "Post"]
    ]

let chanUsers (users: Map<string, UserInfo>) =
  let screenName (u: UserInfo) =
    match u.IsBot with |true -> sprintf "#%s" u.Nick |_ -> u.Nick
  div []
      [ str "Users:"
        ul
          []
          [ for u in users ->
              li [] [str <| screenName u.Value]
          ]]

let root (model: ChannelData) dispatch =
    let users = model.Users |> function | UserCount _ -> Map.empty | UserList list -> list
    div
      [ ClassName "content" ]
      [ h1 [] [ str model.Name ]
        simpleButton "Leave" Leave dispatch
        p [] [str model.Topic]
        postMessage model dispatch
        div
          [ ClassName "columns"]
          [ div
              [ ClassName "column is-four-fifths"]
              [ chanMessages users model.Messages ]
            div
              [ ClassName "column"]
              [ chanUsers users ]
          ]          
      ]