module App.Types

type Msg =
  | ChatDataMsg of Connection.Types.Msg

type Model = {
    currentPage: Router.Route
    chat: Connection.Types.Model
  }
