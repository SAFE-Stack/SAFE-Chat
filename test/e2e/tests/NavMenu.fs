module NavigationPane

open canopy
open Expecto

let all() =

    context "Navigation panel tests"

    before (fun _ ->
        url "http://localhost:8083"
        onn "http://localhost:8083/logon"

        "#nickname" << "Tester-tester"

        click "#login"
        on "http://localhost:8083/#"
    )

    after (fun _ ->
        url "http://localhost:8083/logoff"
    )

    "About link" &&& fun _ ->

        click ".fs-menu a:contains('about')"
        on "http://localhost:8083/#about"

    "Join channel" &&& fun _ ->

        click ".fs-menu button.fs-channel:contains('Demo')"
        on "http://localhost:8083/#channel"

        // ensure there a title and input area
        "Demo" === read ".fs-chat-info h1"
        read ".fs-chat-info span" |> contains "Channel for testing"

        displayed ".fs-message-input"

    "Leave channel channel" &&& fun _ ->

        click ".fs-menu button.fs-channel:contains('Demo')"
        on "http://localhost:8083/#channel"

        displayed ".fs-message-input"
        displayed ".fs-chat-info button[title='Leave']"

        click ".fs-chat-info button[title='Leave']"
        on "http://localhost:8083/#about"

    "Create channel" &&& fun _ ->

        let height selector = (element selector).Size.Height
        let newChannelInput = ".fs-menu input.fs-new-channel"

        sleep()
        0 === (height newChannelInput)
        
        click ".fs-menu button[title='Create New'] i.mdi-plus"
        sleep()

        Expect.isGreaterThan (height newChannelInput) 30 "input is visible"

        // enter text
        newChannelInput << "Harvest"
        click newChannelInput
        press enter

        on "http://localhost:8083/#channel"
        "Harvest" === read ".fs-chat-info h1"

    "Select channel" &&& fun _ ->

        click ".fs-menu button.fs-channel:contains('Demo')"
        click ".fs-menu button.fs-channel:contains('Test')"

        // ensure there a title and input area
        sleep()
        (element ".fs-menu button.selected").Text |> contains "Test"
        
        click ".fs-menu button.fs-channel:contains('Demo')"

        sleep()
        (element ".fs-menu button.selected").Text |> contains "Demo"
