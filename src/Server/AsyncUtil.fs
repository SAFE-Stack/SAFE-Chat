module AsyncUtil

type AsyncResult<'T1, 'T2> = Async<Result<'T1, 'T2>>
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AsyncResult =
    let map f a = async {
        let! result = a
        return result |> Result.map f }
    let bind f a = async {
        match! a with
        | Result.Ok r -> return! f r
        | Result.Error r -> return Result.Error r }
    let liftMap f r = async {
        match r with
        | Result.Ok cch ->  let! r = f cch in return Result.Ok r
        | Result.Error x -> return Result.Error x
    }

module Async =
    let map f a = async {
        let! r = a
        return f r
    }