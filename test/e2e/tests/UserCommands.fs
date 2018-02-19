module UserCommands

open canopy

let all () =

    context "User console commands"

    let sendText message =
        ".fs-message-input input[type='text']" << message
        click ".fs-message-input input[type='text']"
        press enter

    before (fun _ ->
        url "http://localhost:8083"
        onn "http://localhost:8083/logon"

        "#nickname" << "Tester2"
        click "#login"
        on "http://localhost:8083/#"

        ".fs-user #usernick" == "Tester2"
        ".fs-user #userstatus" == ""

        // activate any channel for command input bar to appear (not nice thing)
        click ".fs-menu button.fs-channel:contains('Test')"
        on "http://localhost:8083/#channel"
    )

    after (fun _ ->
        url "http://localhost:8083/logoff"
    )

    "Change nick" &&& fun _ ->
        sendText "Hello all"
        sendText "/nick SuperTester"

        "SuperTester" === read ".fs-user #usernick"

    "Change status" &&& fun _ ->
        sendText "/status The first Human Spectre"

        "The first Human Spectre" === read ".fs-user #userstatus"

    "Change avatar" &&& fun _ ->
        sendText "/avatar http://pictures.org/1.png"

        let avaimg = (element ".fs-user .fs-avatar")

        contains "http://pictures.org/1.png" (avaimg.GetCssValue "background-image")

    "Join channel channel" &&& fun _ ->
        sendText "/join Harvest"

        // check the chat jumps off the channel
        on "http://localhost:8083/#channel"
        "Harvest" === read ".fs-chat-info h1"

    "Leave channel" &&& fun _ ->
        "Test" === read ".fs-chat-info h1"

        sendText "/leave"

        // check the chat jumps off the channel
        on "http://localhost:8083/#about"
