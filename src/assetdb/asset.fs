module AssetDB

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Threading

// asset data
[<AllowNullLiteral>]
type private Data() =
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

// asset cache
let private assets = ConcurrentDictionary<string, Data>()

// asset loader
type Loader = string -> obj

// asset loaders
let private loaders = Dictionary<string, Loader>()

// asset load processor
let private loadProcessor =
    MailboxProcessor.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            msg ()
            return! loop () }

        loop ())

// normalize path
let private normalize path = Path.GetFullPath(path).ToLowerInvariant()

// load asset
let private loadInternal (data: Data) path =
    let ext = Path.GetExtension(path)
    match loaders.TryGetValue(ext) with
    | true, l ->
        loadProcessor.Post(fun () ->
            try
                data.Value <- l path
            with
            | e ->
                printfn "Error loading %s: %s" path e.Message
                data.Event.Set())
    | _ -> failwithf "Unknown asset type %s" ext

// load asset with cache
let private loadInternalCached path =
    assets.GetOrAdd(normalize path, fun path ->
        let data = Data()
        loadInternal data path
        data)

// asset
type Asset<'T>(path: string) =
    [<NonSerialized>]
    let mutable data: Data = null

    // is asset loaded?
    member this.IsReady = data.Value <> null

    // wait for asset to be ready
    member this.Wait () = data.Event.Wait()

    // data accessor
    member this.Data = data.Value :?> 'T

    // path accessor
    member this.Path = path

    // fixup callback
    member private this.Fixup ctx =
        if path <> null then data <- loadInternalCached path

    // load file
    static member LoadAsync path =
        let asset = Asset<'T>(path)
        asset.Fixup()
        asset

    // load file (synchronous version)
    static member Load path =
        let asset = Asset<'T>.LoadAsync path
        asset.Wait()
        if not asset.IsReady then failwithf "Error loading asset %s" path
        asset

// register asset type
let addType ext loader =
    loaders.Add("." + ext, fun path -> box (loader path))

// try to reload asset
let tryReload path =
    match assets.TryGetValue(normalize path) with
    | true, data -> loadInternal data path
    | _ -> ()