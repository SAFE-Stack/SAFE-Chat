//these are similar to C# using statements
open canopy

[<EntryPoint>]
let main _ =

    let executingDir () = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
    configuration.chromeDir <- "..\\..\\packages\\uitests\\Selenium.WebDriver.ChromeDriver\\driver\\win32"

    start chrome

    // define tests
    Logon.all ()
    UserCommands.all ()
    NavigationPane.all ()

    run()
    quit()

    canopy.runner.failedCount
