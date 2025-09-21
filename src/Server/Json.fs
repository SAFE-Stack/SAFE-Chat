module Json

open Thoth.Json.Net

/// Object to Json 
let internal json<'t> (myObj:'t) =   
    // JsonConvert.SerializeObject (myObj, [|jsonConverter|])
    Encode.Auto.toString (myObj, skipNullField = false)

/// Object from Json 
let internal unjson<'t> (jsonString:string)  : Result<'t, string> =  
    // JsonConvert.DeserializeObject<'t> (jsonString, [|jsonConverter|])
    Decode.Auto.fromString<'t> jsonString
