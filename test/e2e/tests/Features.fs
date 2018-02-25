module Features

open canopy
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
        elements Selectors.menuSwitchChannelTitle |> List.map (fun e -> e.Text)
            |> Expect.all "channel is not removed" ((<>) "MyPersonalChannel")

        ()