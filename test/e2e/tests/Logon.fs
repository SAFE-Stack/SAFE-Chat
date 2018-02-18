module Logon

open canopy

let all() =

    context "Logon screen tests"

    before (fun _ ->
        url "http://localhost:8083"
        onn "http://localhost:8083/logon"
    )

    after (fun _ ->
        url "http://localhost:8083/logoff"
    )

    "Regular user login" &&& fun _ ->
        "#nickname" << "Hacker"

        click "#login"
        on "http://localhost:8083/#"

        ".fs-user #usernick" == "Hacker"
        ".fs-user #userstatus" == ""

    "Reload page after login restores session" &&& fun _ ->
        "#nickname" << "fish"

        click "#login"
        on "http://localhost:8083/#"

        reload()
        on "http://localhost:8083/#"

    "Nick contains blank" &&& fun _ ->
        "#nickname" << "Kaidan Alenko"

        click "#login"
        on "http://localhost:8083/#"

        "Kaidan Alenko" === read ".fs-user #usernick"

    // TODO does not accept the user with the same name