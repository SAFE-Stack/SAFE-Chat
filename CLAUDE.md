# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SAFE-Chat is a real-time chat application built with F#, .NET, Akka.NET, and Fable. The architecture follows a client-server model where:

- **Server**: F# with Akka.NET actors for concurrent chat handling, Giraffe for HTTP/WebSocket serving
- **Client**: F# compiled to JavaScript via Fable, using Elmish (MVU pattern) and React
- **Shared**: Common protocol definitions between client and server

## Development Commands

### Prerequisites
- Switch to proper node version: `nvm use` 
- Install dependencies: `yarn`

### Building Production
```bash
# Full production build
./build.cmd

# Manual steps:
cd src/Client
dotnet fable webpack -- -p
cd ../Server  
dotnet build
```

### Development Workflow
```bash
# Start server with hot reload
./dev-server.cmd
# or manually: cd src/Server && dotnet watch run

# Start client dev server (separate terminal)
./dev-cli.cmd  
# or manually: cd src/Client && dotnet fable webpack-dev-server
```

- Server runs on `http://localhost:8083`
- Client dev server runs on `http://localhost:8080` with HMR
- Client proxies API calls to server

### Testing
```bash
# E2E tests (Windows only, requires server running)
cd test/e2e
dotnet restore
dotnet run
```

## Architecture

### Core Communication Flow
1. **WebSocket Protocol**: All real-time communication uses WebSockets after authentication
2. **Shared Protocol**: `src/Shared/ChatProtocol.fs` defines all message types between client/server
3. **Actor System**: Server uses Akka.NET actors for concurrent message handling

### Key Server Components
- **ChatServer.fs**: Main server actor managing channels and user sessions
- **GroupChatChannelActor.fs**: Individual channel actors handling chat messages
- **UserSessionFlow.fs**: Manages individual user WebSocket connections
- **SocketFlow.fs**: WebSocket connection wrapper

### Key Client Components  
- **App.fs**: Main Elmish application entry point with routing
- **State.fs**: Global application state management
- **Channel/**: Channel-specific UI components and state
- **Chat/**: Chat message handling and state

### Authentication
- Supports anonymous users
- OAuth is not restored yet

### Data Persistence
- Akka.NET persistence for chat channels using event sourcing
- Custom JSON event adapter in `AkkaStuff.fs`
- Journal database in `CHAT_DATA/journal.db/`

## File Structure
```
src/
├── Client/           # Fable F# → JavaScript client
│   ├── webpack.config.js
│   ├── public/       # Static assets and generated bundle
│   └── sass/         # Styling
├── Server/           # F# server with Akka.NET
├── Shared/           # Shared protocol definitions
└── Dockerfile        # Container configuration

test/e2e/             # Canopy-based integration tests
```

## Development Notes

### Client Development
- Uses Fable compiler with Webpack for bundling
- Elmish MVU (Model-View-Update) architecture
- Hot module reloading enabled in development
- React components via Fable.React

### Server Development  
- Akka.NET actors for concurrency
- Event sourcing for persistence
- Giraffe for HTTP/WebSocket handling
- `dotnet watch run` provides hot reload

### Protocol Changes
When modifying `src/Client/src/Shared/ChatProtocol.fs`:
1. Update both client and server message handlers
2. Consider backward compatibility for persistent data
3. Test with both OAuth and anonymous authentication flows