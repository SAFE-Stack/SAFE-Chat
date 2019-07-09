//these are similar to C# using statements
open canopy
open canopy.classic
open canopy.runner.classic

[<EntryPoint>]
let main _ =

    let executingDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
    configuration.chromeDir <- executingDir

    start chrome

    // define tests
    Logon.all ()
    UserCommands.all ()
    NavigationPane.all ()
    InputArea.all()
    Features.all()

    run()
    quit()

    failedCount
