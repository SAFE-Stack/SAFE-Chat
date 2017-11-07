module App.Types

type Msg =
  | HomeMsg of Home.Types.Msg
  | ChatDataMsg of Chat.Types.MsgType

type Model = {
    currentPage: Router.Route
    home: Home.Types.Model
    chat: Chat.Types.ChatState
  }
