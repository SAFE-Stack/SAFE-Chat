[![Build Status](https://travis-ci.org/AndrewEgorov/SAFE-Chat.svg?branch=dev)](https://travis-ci.org/AndrewEgorov/SAFE-Chat)

# F#chat

Sample chat application built with .NET 8, F#, Akka.NET and Fable.

![Harvest chat](docs/FsChat-login.gif "Channel view")

## Requirements

* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher
* [Node.js](https://nodejs.org) 22 or higher (works on 8 quite fine though)
* npm (comes with Node.js)
* Global Fable CLI: `dotnet tool install fable --global`

## Building and running the app

* restore dependencies and build application: `./build.sh`
* run the application: `./build.sh start`

More commands:

* `./build.sh clean;build`
* `./build.sh build restore`
* `./build.sh build` -- just build, no restore

Alternatively follow the instruction below:

* **change current folder to `src/Client` folder**: `cd src/Client`
* Install JS dependencies: `yarn`
* Build client bundle: `yarn build`
* **chdir to `src/Server` folder**: `cd ..\Server`
* Install F# dependencies: `dotnet restore`
* Run the server: `dotnet run`
* Head your browser to `http://localhost:8083/`

### Option 2: Modernized Client
* **Use modern build script**: `build-ox.cmd` (Windows) or equivalent bash script
* Or manually:
  * **Move to `src/Client` folder**: `cd src/Client`
  * Install dependencies: `yarn`
  * Build bundle: `yarn build`
  * **Move to `src/Server` folder**: `cd ../Server`
  * Run the server: `dotnet run`

## Developing the app

* Start the server by starting `dotnet run` in `src/Server` folder
* Navigate to `src/Client` folder
* Start Fable daemon and dev server: `yarn start`
* In your browser, open: http://localhost:8080/
* Enjoy HMR (hotload module reload) experience

## Running integration (e2e) tests

E2e tests are based on canopy and webdriver so currently I know it runs on Windows. I have no idea how to run in non-windows environment.

* run the tests: `./build.sh test`
* stop script by typing `q` then pressing `Enter`

or follow these steps:

* start the server
* **Move to `test/e2e` folder**: `cd test\e2e`
* Restore NuGet packages: `dotnet restore`
* run the tests: `dotnet run`

> Tests should be run on clean server, but after server became persistent this condition is usually not met (consider cleaning the src/Server/CHAT_DATA folder ny hands).

## Implementation overview

### Authentication

FsChat supports both *permanent* users, authorized via goodle or github account, and *anonymous* ones, those who provide only nickname.

In order to support the google/fb authentication scenario, fill in the client/secret in the CHAT_DATA/oauth.config file. In case you do not see this file, run the server once and the file will be created automatically.

### Akka streams

FsChat backend is based on Akka.Streams. The entry point is a `GroupChatFlow` module which implements the actor, serving group chat.

`UserSessionFlow` defines the flows for user and control messages, brings everything together and exposes flow for user session.

`AboutFlow` is an example of implementing channel with specific purpose, other than chatting

`ChatServer` is an actor which purpose is to keep the channel list. It's responsible for creating/dropping the channels.

`UserStore` is an actor which purpose is to know all users logged in. It supposed to be made persistent but it does not work for some reason (I created issue).

`SocketFlow` implements a flow decorating the server-side web socket.

### Akkling

Akkling is an unofficial Akka.NET API for F#. It's not just wrapper around Akka.NET API, but introduces some cool concepts such as Effects, typed actors and many more.

### Fable, Elmish

Client is written on F# with the help of Fable and Elmish (library?, framework?). Fable is absolutely mature technology, Elmish is just great.

### Communication protocol

After client is authenticated all communication between client and server is carried via WebSockets. The protocol is defined in `src/Shared/ChatProtocol.fs` file which is shared between client and server projects.

### Persistence

Server implementation demonstrates using Akka Persistance to restore server state after restart. It's based on event sourcing.
However the server destroys the channels when all users are gone. So all channels created by users are non-permanent and will unlikely be restored after restart.

## References

* [Akkling Wiki](https://github.com/Horusiath/Akkling/wiki)
* [Fable Documentation](https://fable.io/docs/)
* [Elmish Documentation](https://elmish.github.io/elmish/)
