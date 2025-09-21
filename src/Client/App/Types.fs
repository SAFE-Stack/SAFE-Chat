module App.Types

type Msg =
  | ChatDataMsg of Connection.Types.Msg

type Model = {
    currentPage: Router.Route
    chatPage: Connection.Types.Model
  }