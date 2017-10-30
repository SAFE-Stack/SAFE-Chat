[<AutoOpen>]
module Common
open System

module private internals =
    // thanks go to tonsky
    let rand = Random(int DateTime.Now.Ticks)
    let encodeTable = "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz"

    let i1len, i2len = 10, 4

    let rec encode (n: uint64) ls =
        function
        | 0 -> ls
        | len -> encode (n / 64UL) (int(n % 64UL)::ls) (len - 1)
    let encodeStr n len =
        let digits = encode n [] len
        digits |> List.map (fun i -> encodeTable.[i]) |> List.toArray |> String

    let rec decodeStr a = function
        | [] -> Some a
        | (ch: char)::tail ->
            match encodeTable.IndexOf ch with
            | -1 -> None
            | i -> decodeStr (a * 64UL + (uint64)i) tail

open internals

type Uuid = {i1: int64; i2: int}
with
    static member Empty = {i1 = -1L; i2 = -1}
    static member New() =
        {
            i1 = DateTime.Now.Ticks &&& ((1L <<< 6 * i1len) - 1L)
            i2 = rand.Next() &&& ((1 <<< 6 * i2len) - 1)
        }
    override this.ToString() =
        match this.i1, this.i2 with
        | -1L, -1 -> "/empty/"
        | i1, i2 -> encodeStr (uint64 i1) i1len + encodeStr (uint64 i2) i2len
    static member TryParse(s: string) =
        let chars = [for c in s -> c]
        if List.length chars = i1len + i2len then
            match chars |> List.take i1len |> decodeStr 0UL, chars |> List.skip i1len |> decodeStr 0UL with
            | Some i1, Some i2 -> Some {i1 = int64 i1; i2 = int i2}
            | _ -> None
        else
            None
