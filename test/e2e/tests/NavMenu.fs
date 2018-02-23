module NavigationPane

open canopy
open Expecto

let all() =

    context "Navigation panel tests"

    before (fun _ ->
        url "http://localhost:8083"
        onn "http://localhost:8083/logon"

        "#nickname" << "Tester-tester"

        click Selectors.loginBtn
        on "http://localhost:8083/#"
    )

    after (fun _ ->
        url "http://localhost:8083/logoff"
    )

    "About link" &&& fun _ ->

        click ".fs-menu a:contains('about')"
        on "http://localhost:8083/#about"

    "Join channel" &&& fun _ ->

        click <| Selectors.switchChannel "Demo"
        on "http://localhost:8083/#channel"

        // ensure there a title and input area
        "Demo" === read Selectors.channelTitle
        read Selectors.channelTopic |> contains "Channel for testing"

        displayed ".fs-message-input"

    "Leave channel channel" &&& fun _ ->

        click <| Selectors.switchChannel "Demo"
        on "http://localhost:8083/#channel"

        displayed Selectors.messageInputPanel
        displayed Selectors.channelLeaveBtn

        click Selectors.channelLeaveBtn
        on "http://localhost:8083/#about"

    "Create channel" &&& fun _ ->

        let height selector = (element selector).Size.Height

        sleep()
        0 === (height Selectors.newChannelInput)
        
        click ".fs-menu button[title='Create New'] i.mdi-plus"
        sleep()

        Expect.isGreaterThan (height Selectors.newChannelInput) 30 "input is visible"

        // enter text
        Selectors.newChannelInput << "Harvest"
        click Selectors.newChannelInput
        press enter

        on "http://localhost:8083/#channel"
        "Harvest" === read Selectors.channelTitle

    "Select channel" &&& fun _ ->

        click <| Selectors.switchChannel "Demo"
        click <| Selectors.switchChannel "Test"

        // ensure there a title and input area
        sleep()
        (element Selectors.selectedChanBtn).Text |> contains "Test"
        
        click <| Selectors.switchChannel "Demo"

        sleep()
        (element Selectors.selectedChanBtn).Text |> contains "Demo"
