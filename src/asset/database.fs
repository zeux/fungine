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

    // fetch strong pointer to asset data from cache by path; assume single-threaded access
    member private this.TryGetUnsafe(path, data: byref<Data>) =
        let mutable wrdata = null
        assets.TryGetValue(path, &wrdata) && wrdata.TryGetTarget(&data)

    // get existing asset or add a new one by path, try both raw and normalized paths; assume single-threaded access
    // returns true if a new entry was inserted
    member private this.GetOrAddUnsafe(path, data: byref<Data>) =
        // try raw path
        if this.TryGetUnsafe(path, &data) then false
        else
            // try normalized path
            let npath = Path.GetFullPath(path).ToLowerInvariant()

            if this.TryGetUnsafe(npath, &data) then false
            else
                data <- Data()

                // insert new entry for both raw and normalized paths
                let wr = WeakReference<Data>(data)

                assets.[path] <- wr
                assets.[npath] <- wr

                true

    // get existing asset or add a new one by path, try both raw and normalized paths; assume single-threaded access
    // returns true if a new entry was inserted
    member private this.GetOrAdd(path, data: byref<Data>) =
        Monitor.Enter(assets)
        try
            this.GetOrAddUnsafe(path, &data)
        finally
            Monitor.Exit(assets)

    // load asset
    member internal this.Load(path, loader) =
        let mutable data = null

        // add asset to database and call loader if necessary
        if this.GetOrAdd(path, &data) then
            loader path data

        data