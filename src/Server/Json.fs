module Json

open Thoth.Json.Net

let inline json<'T> (x: 'T) = Encode.Auto.toString(0, x)
let inline unjson<'T> json = Decode.Auto.unsafeFromString<'T>(json)
