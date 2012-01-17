namespace Asset

open System
open System.Threading
open System.Runtime.ExceptionServices

// asset data
[<AllowNullLiteral>]
type internal Asset() =
    let event = new ManualResetEventSlim()
    let mutable value: obj = null
    let mutable error: ExceptionDispatchInfo = null

    // asset load completion event
    member this.Event = event

    // is asset finished loading
    member this.IsReady =
        value <> null

    // asset value
    member this.Value =
        if error <> null then error.Throw()
        if value = null then failwith "Asset is not loaded"
        value

    // set asset value
    member this.SetResult v =
        value <- v
        event.Set()

    // set asset exception
    member this.SetException e =
        error <- ExceptionDispatchInfo.Capture(e)
        event.Set()