namespace Asset

open System
open System.Collections.Generic
open System.IO
open System.Threading

// asset data
[<AllowNullLiteral>]
type internal Data() =
    let event = new ManualResetEventSlim()
    let mutable value: obj = null

    // asset load completion event
    member this.Event = event

    // asset value
    member this.Value
        with get () = value
        and set v =
            value <- v
            event.Set()

// asset database
type Database() =
    let assets = Dictionary<string, WeakReference<Data>>()

    // normalize path
    let normalize path = Path.GetFullPath(path).ToLowerInvariant()

    // fetch strong pointer to asset data from cache by path; assume single-threaded access
    member private this.TryGetUnsafe(path, data: byref<Data>) =
        let mutable wrdata = null
        assets.TryGetValue(path, &wrdata) && wrdata.TryGetTarget(&data)

    // get existing asset or add a new one by path, try both raw and normalized paths; assume single-threaded access
    // returns true if the asset was found
    member private this.GetOrAddUnsafe(path, data: byref<Data>) =
        // try raw path
        if this.TryGetUnsafe(path, &data) then true
        else
            // try normalized path
            let npath = normalize path

            if this.TryGetUnsafe(npath, &data) then true
            else
                data <- Data()

                // insert new entry for both raw and normalized paths
                let wr = WeakReference<Data>(data)

                assets.[path] <- wr
                assets.[npath] <- wr

                false

    // get existing asset or add a new one by path, try both raw and normalized paths; assume single-threaded access
    // returns true if the asset was found
    member internal this.GetOrAdd(path, data: byref<Data>) =
        Monitor.Enter(assets)
        try
            this.GetOrAddUnsafe(path, &data)
        finally
            Monitor.Exit(assets)

    // get existing asset
    member internal this.TryFind(path, data: byref<Data>) =
        Monitor.Enter(assets)
        try
            this.TryGetUnsafe(normalize path, &data)
        finally
            Monitor.Exit(assets)