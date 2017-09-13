#r @"C:\Users\olegz\.nuget\packages\netstandard.library.netframework\2.0.0-preview2-25405-01\build\net461\lib\netstandard.dll"


#r @"c:\projects\fschat\src\app\bin\Debug\net461\fschatapp.dll"
#r @"src\host\bin\Debug\net461\fschathost.exe"

#r @"C:\Users\olegz\.nuget\packages\giraffe\0.1.0-beta-100\lib\net461\Giraffe.dll"
#r @"C:\projects\fschat\src\host\bin\Debug\net461\Microsoft.AspNetCore.Http.Abstractions.dll"

let a = fschat.app.webApp

ignore <| fschathost.App.main [||]

