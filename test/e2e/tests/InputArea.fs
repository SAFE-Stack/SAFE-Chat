module InputArea

open canopy

let switchChannel name =
    click <| Selectors.switchChannel name
    on "http://localhost:8083/#channel"
    (element Selectors.selectedChanBtn).Text |> contains name

let all () =
    context "User console commands"

    before (fun _ ->
        url "http://localhost:8083"
        onn "http://localhost:8083/logon"

        "#nickname" << "InputAreaTester"
        click Selectors.loginBtn
        on "http://localhost:8083/#"

        Selectors.userNick == "InputAreaTester"
    )

    after (fun _ ->
        url "http://localhost:8083/logoff"
    )

    "Input area is visible in channel view" &&& fun _ ->

        notDisplayed Selectors.messageInputPanel

        switchChannel "Test"

        displayed Selectors.messageInputPanel

    "Type and send text, input gets clean" &&& fun _ ->

        switchChannel "Test"

        Selectors.messageInputText << "Hello world"
        Selectors.messageInputText == "Hello world"

        click Selectors.messageInputPanel
        press enter
        sleep()
        
        Selectors.messageInputText == ""

    "Input message stored per channel" &&& fun _ ->

        switchChannel "Test"
        Selectors.messageInputText << "test channel"

        switchChannel "Demo"
        Selectors.messageInputText == ""
        Selectors.messageInputText << "hi demo"

        switchChannel "Test"
        Selectors.messageInputText == "test channel"

        switchChannel "Demo"
        Selectors.messageInputText == "hi demo"

    "Type and send text" &&& fun _ ->

        switchChannel "Test"

        Selectors.messageInputText << "Hello world"
        click Selectors.messageSendBtn

        read ".fs-chat .fs-messages div[class*='fs-message user'] div p" |> contains "Hello world"

        sleep()
        Selectors.messageInputText == ""