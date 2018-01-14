module Channel.View

open Fable.Core.JsInterop
open Fable.Helpers.React
open Props

open Types
open UserAvatar.Types

let private formatTs (ts: System.DateTime) =
  match (System.DateTime.Now - ts) with
  | diff when diff.TotalMinutes < 1.0 -> "a few seconds ago"
  | diff when diff.TotalMinutes < 30.0 -> sprintf "%i minutes ago" (int diff.TotalMinutes)
  | diff when diff.TotalHours <= 12.0 -> ts.ToShortTimeString()
  | diff when diff.TotalDays <= 5.0 -> sprintf "%i days ago" (int diff.TotalDays)
  | _ -> ts.ToShortDateString()

let messageInput dispatch model =
  div
    [ ClassName "fs-message-input" ]
    [ input
        [ Type "text"
          Placeholder "Type the message here..."
          Value model.PostText
          OnChange (fun ev -> !!ev.target?value |> (SetPostText >> dispatch))
          OnKeyPress (fun ev -> if !!ev.which = 13 || !!ev.keyCode = 13 then dispatch PostText)
        ]
      button
        [ ClassName "btn" ]
        [ i [ ClassName "mdi mdi-send mdi-24px"
              OnClick (fun _ -> dispatch PostText) ] [] ]
    ]

let chanUsers (users: Map<string, UserInfo>) =
  let screenName (u: UserInfo) =
    match u.IsBot with |true -> sprintf "#%s" u.Nick |_ -> u.Nick
  div [ ClassName "userlist" ]
      [ str "Users:"
        ul
          []
          [ for u in users ->
              li [] [str <| screenName u.Value]
          ]]

let chatInfo dispatch (model: ChannelData) =
  div
    [ ClassName "fs-chat-info" ]
    [ h1
        [] [ str model.Name ]
      span
        [] [ str model.Topic ]
      button
        [ ClassName "btn"
          Title "Leave"
          OnClick (fun _ -> dispatch Leave) ]
        [ i [ ClassName "mdi mdi-door-closed mdi-18px" ] []]
    ]

let messageList isMe (messages: Message list) =
    div
      [ ClassName "fs-messages" ]
      [ for m in messages ->
          match m.Content with
          | UserMessage (text, user) ->
              div
                [ classList ["fs-message", true; "user", isMe user.Nick ] ]
                [ div
                    []
                    [ p [] [ str text ]
                      h5  []
                          [ span [ClassName "user"] [str user.Nick]
                            span [ClassName "time"] [str <| formatTs m.Ts ]] ]
                  UserAvatar.View.root (PhotoUrl "https://pbs.twimg.com/profile_images/2191150324/Avatar_Shepard_400x400.jpg")
                ]

          | SystemMessage text ->
              blockquote
                [ ClassName ""]
                [ str text; str " "
                  small [] [str <| formatTs m.Ts] ]
      ]


let root isMe (model: ChannelData) dispatch =
    // let users = model.Users |> function | UserCount _ -> Map.empty | UserList list -> list
    // let getUser author =
    //     users |> Map.tryFind author
    //     |> function | Some u -> u | _ -> {Nick = author; IsBot = false; Online = false}

    [ chatInfo dispatch model
      div [ ClassName "fs-splitter" ] []
      messageList isMe  model.Messages
      div [ ClassName "fs-splitter" ] []
      messageInput dispatch model
     ]
