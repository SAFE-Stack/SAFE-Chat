#r "paket:
    nuget Xake ~> 1.1 prerelease //"

#load ".fake/build.fsx/intellisense.fsx"

open Xake
open Xake.Tasks

let clientBundle = "src/Client/public/bundle.js"
let serverDllRel = "bin/Debug/netcoreapp2.0/fschathost.dll"
let serverDll = "src/Server/" + serverDllRel

let shellx cmd folder =
    let command::arglist | OtherwiseFail(command,arglist) = (cmd: string).Split(' ') |> List.ofArray
    shell {
        cmd command
        args arglist
        workdir folder
        failonerror
    } |> Ignore

do xakeScript {
    consolelog Diag

    rules [
        // main (default) target is to sequentially restore deps and build
        "main" <<< [ "restore"; "build" ]

        // cleans the build artifacts
        "clean" => recipe {
            do! rm {file "src/Client/bundle.*"}
            do! rm {dir "src/*/bin/*"; verbose }
            do! rm {dir "src/*/obj/*"; verbose }
        }

        // restores packages and node modules
        "restore" => recipe {
            do! "src/Client" |> shellx "yarn"
            do! "src/Server" |> shellx "dotnet restore"
        }

        // build the client bundle
        clientBundle ..> recipe {
            // record dependencies so that Xake will track the changes
            let! files = getFiles (fileset {
                basedir "src/Client"
                includes "**/*.fs"
                includes "**/*.*css"
                includes "webpack.config.js"
                includes "yarn.lock"
                includes "client.fsproj"
            })
            do! needFiles files
            do! "src/Client" |> shellx "yarn build"
        }

        serverDll ..> recipe {
            do! need [clientBundle]
            do! "src/Server" |> shellx "dotnet build"
        }

        // build the application
        "build" <== [serverDll]

        // start server and browser in parallel
        "start" <== [ "start:server"; "start:browser" ]

        // starts the server and runs
        "test" <== [ "start:server"; "test-e2e" ]

        "start:server" => recipe {
            do! need ["build"]
            do! "src/Server" |> shellx ("dotnet " + serverDllRel)
        }
        "test-e2e" => shellx "dotnet run" "test/e2e"
        // opens the application in browser, windows only
        "start:browser" => shellx "start http://localhost:8083" "."
    ]
}