module InputArea

open canopy

let inputTextSelector = ".fs-message-input input[type='text']"

let all () =
    context "User console commands"

    before (fun _ ->
        url "http://localhost:8083"
        onn "http://localhost:8083/logon"

        "#nickname" << "InputAreaTester"
        click "#login"
        on "http://localhost:8083/#"

        ".fs-user #usernick" == "InputAreaTester"
    )

    after (fun _ ->
        url "http://localhost:8083/logoff"
    )

    "Input area is visible in channel view" &&& fun _ ->

        notDisplayed ".fs-message-input"

        click ".fs-menu button.fs-channel:contains('Test')"
        on "http://localhost:8083/#channel"

        displayed ".fs-message-input"

    "Type and send text, input gets clean" &&& fun _ ->

        click ".fs-menu button.fs-channel:contains('Test')"
        on "http://localhost:8083/#channel"

        inputTextSelector << "Hello world"
        click ".fs-message-input"
        inputTextSelector == "Hello world"

        press enter
        sleep()
        
        inputTextSelector == ""

    // TODO input is preserved on channel switch