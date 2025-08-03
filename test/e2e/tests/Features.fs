module Features

open canopy.runner.classic
open canopy.classic

open Expecto
open Expecto.Flip

let all () =
    context "Miscelaneous features"

    before (fun _ ->
        Routines.loginAnonymous "Tester2"
    )

    after (fun _ ->
        Routines.logout()
    )

    "Automatically drop channel when last user left" &&& fun _ ->

        Routines.joinChannel "MyPersonalChannel"

        elements Selectors.menuSwitchChannelTitle |> List.map (fun e -> e.Text)
            |> Expect.contains "newly added channel" "MyPersonalChannel"

        click Selectors.channelLeaveBtn

        sleep ()

        elements Selectors.menuSwitchChannelTitle |> List.map (fun e -> e.Text)
            |> Expect.all "channel is not removed" ((<>) "MyPersonalChannel")
        sleep ()    // FIXME maybe the reason is incorrect events order

        ()

    "Do not drop channel with autoRemove set to False" &&& fun _ ->

        Routines.joinChannel "Test"

        click Selectors.channelLeaveBtn

        elements Selectors.menuSwitchChannelTitle |> List.map (fun e -> e.Text)
            |> Expect.contains "newly added channel" "Test"

        ()