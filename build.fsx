// xake build file

#r "nuget: Xake, 2.3.0"


// Notice: this is not a traditional FAKE script.
// Instead it uses Xake module to define build targets and rules in CMAKE fashion.
// See https://github.com/FakeBuild/Xake/wiki for more details.

open Xake
open Xake.Tasks

let clientBundle = "src/Client/public/index.html"
let serverDllRel = "bin/Debug/net8.0/fschathost.dll"
let serverDll = "src/Server/" + serverDllRel

do xakeScript {
    consolelog Diag
    rules [
        // main (default) target is to sequentially restore deps and build
        "main" <<< [ "restore"; "build" ]

        // cleans the build artifacts
        "clean" => recipe {
            do! rm {dir "src/Client/public"}
            do! rm {file "src/Client/**/*.fs.js"}
            do! rm {dir "src/*/bin/*"; verbose }
            do! rm {dir "src/*/obj/*"; verbose }
        }

        // restores packages and node modules
        "restore" => recipe {
            do! sh "yarn" { workdir "src/Client" }
            do! sh "dotnet restore" { workdir "src/Server" }
        }

        // build the client bundle
        clientBundle ..> recipe {
            // record dependencies so that Xake will track the changes
            do! dependsOn (fileset {
                basedir "src/Client"
                includes "**/*.fs"
                includes "**/*.*css"
                includes "vite.config.js"
                includes "yarn.lock"
                includes "client.fsproj"
            })
            do! sh "yarn build" { workdir "src/Client" }
        }

        serverDll ..> recipe {
            do! need [clientBundle]
            do! sh "dotnet build" { workdir "src/Server" }
        }

        // build the application
        "build" <== [serverDll]

        // start server and browser in parallel
        "start" <== [ "start:server"; "start:browser" ]

        // starts the server and runs
        "test" <== [ "start:server"; "test-e2e" ]

        "start:server" => recipe {
            do! need ["build"]
            do! sh ("dotnet " + serverDllRel) { workdir "src/Server" }
        }
        "test-e2e" => sh "dotnet run" { workdir "test/e2e" }
        // opens the application in browser, macos only, for windows this has to be replaced to start http://...
        "start:browser" => sh "open http://localhost:8083" { workdir "." }
    ]
}