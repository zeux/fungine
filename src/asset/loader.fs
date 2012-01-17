namespace Asset

open System
open System.Collections.Generic
open System.IO
open System.Threading

// asset loaders
type LoaderMap = IDictionary<string, string -> Loader -> obj>

// asset loader
and Loader(database: Database, loaders: LoaderMap) =
    // background loading agent
    let agent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop () = async {
                let! msg = inbox.Receive()
                msg ()
                return! loop () }
    
            loop ())

    // execute asset operation with exception capture
    let protectOp path (data: Asset) op =
        try
            op ()
        with e ->
            printfn "Error loading %s: %s" path e.Message
            data.SetException(e)

    // force load asset data by path
    member private this.ForceLoadDataAsync(path, data: Asset) =
        let ext = Path.GetExtension(path)

        match loaders.TryGetValue(ext) with
        | true, l ->
            agent.Post(fun () -> protectOp path data (fun () -> data.SetResult(l path this)))
        | _ ->
            protectOp path data (fun () -> failwithf "Unknown asset type %s" ext)

    // load asset data by path
    member internal this.LoadDataAsync path =
        let mutable data = null
        if not (database.GetOrAdd(path, &data)) then
            this.ForceLoadDataAsync(path, data)
        data

    // load asset by path
    member this.LoadAsync path =
        let data = this.LoadDataAsync path

        Ref(path, data)

    // load asset by path; synchronous version
    member this.Load path =
        let ref = this.LoadAsync path
        ref.Wait()
        ref

    // try to reload the asset by path
    member this.TryReload path =
        let mutable data = null
        if database.TryFind(path, &data) then
            this.ForceLoadDataAsync(path, data)

// asset reference
and Ref<'T> internal(path: string, data: Asset) =
    [<NonSerialized>]
    let mutable data = data

    // construct asset ref from path; this ref is invalid until fixup
    new (path) = Ref(path, null)

    // is asset loaded?
    member this.IsReady = data.IsReady

    // wait for asset to be ready
    member this.Wait () : unit =
        data.Event.Wait()
        ignore data.Value // throw pending exceptions if any

    // value accessor
    member this.Value = data.Value :?> 'T

    // path accessor
    member this.Path = path

    // asset fixup
    member private this.Fixup ctx =
        data <- Core.Serialization.Fixup.Get<Loader>(ctx).LoadDataAsync(path)