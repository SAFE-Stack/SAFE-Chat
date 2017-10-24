# F#chat

Sample chat application built with netcore, F#, Akka.net and Fable.

## Requirements

* [dotnet SDK](https://www.microsoft.com/net/download/core) 2.0.0 or higher
* [node.js](https://nodejs.org) 4.8.2 or higher
* yarn (`npm i yarn -g`)
* npm5: JS package manager


## Building and running the app

* Install JS dependencies: `yarn`
* **Move to `src` folder**: `cd src`
* Install F# dependencies: `dotnet restore`
* Start Fable daemon and [Webpack](https://webpack.js.org/) dev server: `dotnet fable npm-start`
* In your browser, open: http://localhost:8080/

## References

* [paket and dotnet cli](https://fsprojects.github.io/Paket/paket-and-dotnet-cli.html)
* ...
