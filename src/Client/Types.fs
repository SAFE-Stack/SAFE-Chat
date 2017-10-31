module App.Types

type Msg =
  | HomeMsg of Home.Types.Msg
  | ChatDataMsg of ChatData.Types.Msg

type Model = {
    currentPage: Router.Route
    home: Home.Types.Model
    chat: ChatData.Types.Chat
  }
