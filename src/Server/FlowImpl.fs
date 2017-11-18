module FlowImpl

open Akka.Streams
open Akka.Streams.Dsl

// Creates a flow which multiplexes input and passes via several worker flows, then merges the flows
let split2 worker1 worker2 combine =
    Akkling.Streams.Graph.create2 combine (fun b w1 w2 ->
        let broadcast = Broadcast<'TIn> 2 |> b.Add
        let merge = Merge<'TOut> 2 |> b.Add

        ((b.From (broadcast.Out(0)) ).Via (w1: FlowShape<_,_>)).To (merge.In(0)) |> ignore
        ((b.From (broadcast.Out(1)) ).Via (w2: FlowShape<_,_>)).To (merge.In(1)) |> ignore

        FlowShape(broadcast.In, merge.Out)
    ) worker1 worker2
    |> Flow.FromGraph
