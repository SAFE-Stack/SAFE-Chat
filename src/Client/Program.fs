module Program

open Elmish
open Elmish.Navigation
open Fable.Core.JsInterop

importAll "./sass/app.scss"

open Elmish.Debug
open Elmish.HMR

open App.State

// App
Program.mkProgram init update App.View.root
|> Program.toNavigable (UrlParser.parseHash Router.route) urlUpdate
#if DEBUG
|> Program.withDebugger
#endif
|> Program.withReactBatched "elmish-app"
|> Program.run
