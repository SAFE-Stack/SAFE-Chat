module Json

open System.IO
open System.Runtime.Serialization.Json
open System.Xml
open System.Text

/// Object to Json 
let internal json<'t> (myObj:'t) =   
    use ms = new MemoryStream() 
    DataContractJsonSerializer(typeof<'t>).WriteObject(ms, myObj) 
    Encoding.Default.GetString(ms.ToArray()) 


/// Object from Json 
let internal unjson<'t> (jsonString:string)  : 't =  
    use ms = new MemoryStream(ASCIIEncoding.Default.GetBytes(jsonString)) 
    let obj = DataContractJsonSerializer(typeof<'t>).ReadObject(ms) 
    obj :?> 't
