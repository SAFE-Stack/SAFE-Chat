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

    "Nick contains non-ascii characters" &&& fun _ ->
        "#nickname" << "Иван Петров"

        click "#login"
        on "http://localhost:8083/#"

        "Иван Петров" === read ".fs-user #usernick"


    "Logoff button is functioning" &&& fun _ ->
        "#nickname" << "Godzilla"

        click "#login"
        on "http://localhost:8083/#"

        click "#logout"
        on "http://localhost:8083/logon"
        

    // TODO does not accept the user with the same name