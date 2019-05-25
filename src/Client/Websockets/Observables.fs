namespace Fable.Websockets
// Copied from http://www.fssnip.net/2E/title/ObservableSubject

module Observables =
    open System
    open System.Collections.Generic

    #if FABLE_COMPILER 
    let inline lock _ action = action ()
    #endif
    

    type public Subject<'T> () =
       let sync = obj()
       let mutable stopped = false
       let observers = List<IObserver<'T>>()
       let iter f = observers |> Seq.iter f
       let onCompleted () =
          if not stopped then
             stopped <- true
             iter (fun observer -> observer.OnCompleted())
       let onError ex () =
          if not stopped then
             stopped <- true
             iter (fun observer -> observer.OnError(ex))

       let next value () =
          if not stopped then
             iter (fun observer -> observer.OnNext(value))
       let remove observer () =
          observers.Remove observer |> ignore
       member public x.Next value = lock sync <| next value
       member public x.Error ex = lock sync <| onError ex
       member x.Completed () = lock sync <| onCompleted
       interface IObserver<'T> with
          member x.OnCompleted() = x.Completed()
          member x.OnError ex = x.Error ex
          member x.OnNext value = x.Next value
       interface IObservable<'T> with
          member this.Subscribe(observer:IObserver<'T>) =
             observers.Add observer
             { new IDisposable with
                member this.Dispose() =
                   lock sync <| remove observer
             }


      type CompositeDisposable (values: IDisposable seq) =             
            member private this.Values = values            
                                
            interface IDisposable with
                  member this.Dispose () = this.Values |> Seq.iter (fun x-> x.Dispose())                  

      module CompositeDisposable =
            let public ofSeq sequence = new CompositeDisposable(sequence)