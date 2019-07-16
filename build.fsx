#r "paket:
    nuget Xake ~> 1.1 prerelease //"

#load ".fake/build.fsx/intellisense.fsx"

open Xake
open Xake.Tasks

let clientBundle = "src/Client/public/bundle.js"
let serverDllRel = "bin/Debug/netcoreapp2.0/fschathost.dll"
let serverDll = "src/Server/" + serverDllRel

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
            do! (Ignore <| shell {
                cmd "yarn"; workdir "src/Client"
                failonerror })
            do! (Ignore <| shell {
                cmd "dotnet"; args ["restore"]; workdir "src/Server"
                failonerror })
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
            
            do! (shell {
                cmd "yarn"; args ["build"]; workdir "src/Client"
                failonerror
            } |> Ignore)
        }

        serverDll ..> recipe {
            do! need [clientBundle]
            do! (shell {
                cmd "dotnet"
                args ["build"]
                workdir "src/Server"
                failonerror
            } |> Ignore)
        }

        // build the application
        "build" <== [serverDll]

        // start in parallel
        "start" <== [ "start:server"; "start:browser" ]

        "start:server" => recipe {
            do! need ["build"]
            do! (shell {
                cmd "dotnet"
                args [serverDllRel]
                workdir "src/Server"
                failonerror
            } |> Ignore)
        }
        "start:browser" => recipe {
            do! (shell {
                cmd "start"
                args ["http://localhost:8083"]
            } |> Ignore)
        }
    ]
}