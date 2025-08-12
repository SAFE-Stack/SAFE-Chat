module Channel.View

open Fable.Core.JsInterop
open Fable.React

open Fable.React.Props
open Types

open Fable.ReactMarkdownImport

let private formatTs (ts: System.DateTime) =
  match (System.DateTime.Now - ts) with
  | diff when diff.TotalMinutes < 1.0 -> "a few seconds ago"
  | diff when diff.TotalMinutes < 30.0 -> sprintf "%i minutes ago" (int diff.TotalMinutes)
  | diff when diff.TotalHours <= 12.0 -> ts.ToShortTimeString()
  | diff when diff.TotalDays <= 5.0 -> sprintf "%i days ago" (int diff.TotalDays)
  | _ -> ts.ToShortDateString()

let inline valueOrDefault value =
    Ref <| (fun e -> if e |> isNull |> not && !!e?value <> !!value then e?value <- !!value)

let messageInput dispatch model =
  div
    [ ClassName "fs-message-input" ]
    [ input
        [ Type "text"
          Placeholder "Type the message here..."
          valueOrDefault model.PostText
          OnChange (fun ev -> !!ev.target?value |> (SetPostText >> dispatch))
          OnKeyPress (fun ev -> if !!ev.key = "Enter" then dispatch PostText)
        ]
      button
        [ ClassName "btn" ]
        [ i [ ClassName "mdi mdi-send mdi-24px"
              OnClick (fun _ -> dispatch PostText) ] [] ]
    ]

let chanUsers (users: Map<string, UserInfo>) =
  let screenName (u: UserInfo) =
    match u.IsBot with |true -> sprintf "#%s" u.Nick |_ -> u.Nick
  
  let userItem (u: UserInfo) =
    li [ classList ["user-item", true; "online", u.Online; "offline", not u.Online; "me", u.isMe] ]
       [ span [ ClassName "user-status" ]
              [ i [ classList ["mdi", true; "mdi-circle", true; "online", u.Online; "offline", not u.Online] ] [] ]
         span [ ClassName "user-nick" ] [ str <| screenName u ]
         if not (System.String.IsNullOrEmpty(u.Status)) then
           span [ ClassName "user-status-text" ] [ str u.Status ]
       ]
  
  div [ ClassName "userlist" ]
      [ h4 [] [ str "Users:" ]
        ul [ ClassName "user-list" ]
          [ for KeyValue(_, u) in users ->
              userItem u
          ]]

let chatInfo dispatch (model: Model) =
  div
    [ ClassName "fs-chat-info" ]
    [ h1
        [] [ str model.Info.Name ]
      span
        [] [ str model.Info.Topic ]
      button
        [ Id "leaveChannel"
          ClassName "btn"
          Title "Leave"
          OnClick (fun _ -> dispatch Leave) ]
        [ i [ ClassName "mdi mdi-door-closed mdi-18px" ] []]
    ]

let message (text: string) =
    [ reactMarkdownText text [] ]

let messageList (messages: Message Envelope list) =
    div
      [ ClassName "fs-messages" ]
      [ for m in messages ->
          match m.Content with
          | UserMessage (text, user) ->
              // Browser.Dom.console.warn (sprintf "%A %A" text user)
              div
                [ classList ["fs-message", true; "user", user.isMe ] ]
                [ div
                    []
                    [ yield! message text
                      yield h5  []
                          [ span [ClassName "user"] [str user.Nick]
                            span [ClassName "time"] [str <| formatTs m.Ts ]] ]
                  UserAvatar.View.root user.ImageUrl
                ]

          | SystemMessage text ->
              blockquote
                [ ClassName ""]
                [ str text; str " "
                  small [] [str <| formatTs m.Ts] ]
      ]


let root (model: Model) dispatch =
    [ chatInfo dispatch model
      div [ ClassName "fs-splitter" ] []
      div [ ClassName "fs-chat-content" ]
        [ div [ ClassName "fs-messages-container" ]
            [ messageList model.Messages ]
          div [ ClassName "fs-users-sidebar" ]
            [ chanUsers model.Users ]
        ]
      div [ ClassName "fs-splitter" ] []
      messageInput dispatch model
    ]
