namespace fschat.Models

[<CLIMutable>]
type UserSession =
    {
        UserName: string
        // token
        Channels: string list // list of channels I'm in
    }
