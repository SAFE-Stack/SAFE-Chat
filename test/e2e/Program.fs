//these are similar to C# using statements
open canopy
open runner
open System

let executingDir () = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
configuration.chromeDir <- "..\\..\\packages\\uitests\\Selenium.WebDriver.ChromeDriver\\driver\\win32"

//start an instance of the firefox browser
start chrome

//this is how you define a test
"taking canopy for a spin" &&& fun _ ->
    //this is an F# function body, it's whitespace enforced

    //go to url
    url "http://localhost:8083"
    on "http://localhost:8083/logon"

    //assert that the element with an id of 'welcome' has
    //the text 'Welcome'
    "#nickname" << "Olegz2"

    click "#login"
    on "http://localhost:8083/#"

    ".fs-user h3" == "Olegz2"

    url "http://localhost:8083/logoff"

//run all tests
run()

printfn "press [enter] to exit"
System.Console.ReadLine() |> ignore

quit()
