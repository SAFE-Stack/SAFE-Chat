# F#chat

Sample chat application built with netcore, F#, Akka.net and Fable.

## Extensibility

The random ideas for extending the chat:

* [x] store internally uuid and channels for the user, let application specific user info be a parameter to chat
* [] store ActorSystem in server state (simplify ServerApi then)
* [] reimplement echo actor using Flow<>
* [] chan name is sufficiently good identifier, consider removing channel id in favor of name




## References

* [paket and dotnet cli](https://fsprojects.github.io/Paket/paket-and-dotnet-cli.html)
* ...
