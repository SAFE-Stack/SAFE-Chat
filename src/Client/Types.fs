module App.Types

type Msg =
  | ChatDataMsg of Chat.Types.MsgType

type Model = {
    currentPage: Router.Route
    chat: Chat.Types.ChatState
  }
