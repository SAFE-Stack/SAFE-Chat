# F#chat

Sample chat application built with netcore, F#, Akka.net and Fable.

## Requirements

* [dotnet SDK](https://www.microsoft.com/net/download/core) 2.0.0 or higher
* [node.js](https://nodejs.org) 4.8.2 or higher
* yarn (`npm i yarn -g`)
* npm5: JS package manager

## Building and running the app

* Install JS dependencies: `yarn`
* **Move to `src/Client` folder**: `cd src\Client`
* Install F# dependencies: `dotnet restore`
* Build client bundle: `dotnet fable webpack -p`
* **Move to `src/Server` folder**: `cd ..\Server`
* Install F# dependencies: `dotnet restore`
* Run the server: `dotnet run`
* Head your browser to `http://localhost:8083/`

## Developing the app

* Start the server (see instruction above)
* **Move to `src/Client` folder**: `cd src\Client`
* Start Fable daemon and [Webpack](https://webpack.js.org/) dev server: `dotnet fable webpack-dev-server`
* In your browser, open: http://localhost:8080/
* Enjoy HMR (hotload module reload) experience

> Notice: logon screen will redirect your browser to localhost:8083 (per configuration of server auth handler), you have manually change the port to 8080

## References

* [paket and dotnet cli](https://fsprojects.github.io/Paket/paket-and-dotnet-cli.html)
* ...
