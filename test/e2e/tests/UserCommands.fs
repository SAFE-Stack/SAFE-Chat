module UserCommands

open canopy

let typeSlow: string -> unit = Seq.iter(string >> press)

let all () =

    context "Update user data commands"

    let sendText message =
        // click ".fs-message-input input[type='text']"
        // typeSlow message
        ".fs-message-input input[type='text']" << message
        click ".fs-message-input .btn"

    before (fun _ ->
        url "http://localhost:8083"
        onn "http://localhost:8083/logon"

        "#nickname" << "Tester2"
        click "#login"
        on "http://localhost:8083/#"
    )

    after (fun _ ->
        url "http://localhost:8083/logoff"
    )

    "Change nick" &&&& fun _ ->

        ".fs-user #usernick" == "Tester2"
        ".fs-user #userstatus" == ""

        click ".fs-menu button.fs-channel:contains('Test')"
        on "http://localhost:8083/#channel"

        sendText "Hello all"
        sendText "/nick SuperTester"

        "SuperTester" === read ".fs-user #usernick"
