namespace Asset

open System
open System.Collections.Generic
open System.IO
open System.Threading

// asset database
type Database() =
    let assets = Dictionary<string, WeakReference<Asset>>()

    // normalize path
    let normalize path = Path.GetFullPath(path).ToLowerInvariant()

    // fetch strong pointer to asset data from cache by path; assume single-threaded access
    member private this.TryGetUnsafe(path, data: byref<Asset>) =
        let mutable wrdata = null
        assets.TryGetValue(path, &wrdata) && wrdata.TryGetTarget(&data)

    // get existing asset or add a new one by path, try both raw and normalized paths; assume single-threaded access
    // returns true if the asset was found
    member private this.GetOrAddUnsafe(path, data: byref<Asset>) =
        // try raw path
        if this.TryGetUnsafe(path, &data) then true
        else
            // try normalized path
            let npath = normalize path

            if this.TryGetUnsafe(npath, &data) then true
            else
                data <- Asset()

                // insert new entry for both raw and normalized paths
                let wr = WeakReference<Asset>(data)

                assets.[path] <- wr
                assets.[npath] <- wr

                false

    // get existing asset or add a new one by path, try both raw and normalized paths; assume single-threaded access
    // returns true if the asset was found
    member internal this.GetOrAdd(path, data: byref<Asset>) =
        Monitor.Enter(assets)
        try
            this.GetOrAddUnsafe(path, &data)
        finally
            Monitor.Exit(assets)

    // get existing asset
    member internal this.TryFind(path, data: byref<Asset>) =
        Monitor.Enter(assets)
        try
            this.TryGetUnsafe(normalize path, &data)
        finally
            Monitor.Exit(assets)