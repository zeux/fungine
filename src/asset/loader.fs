namespace Asset

open System
open System.Collections.Generic
open System.IO
open System.Threading

// asset loaders
type LoaderMap = IDictionary<string, string -> obj>

// asset loader
type Loader(database: Database, loaders: LoaderMap) =
    let agent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop () = async {
                let! msg = inbox.Receive()
                msg ()
                return! loop () }
    
            loop ())

    // force load asset data by path
    member private this.ForceLoadDataAsync(path, data: Data) =
        let ext = Path.GetExtension(path)

        match loaders.TryGetValue(ext) with
        | true, l ->
            agent.Post(fun () ->
                try
                    data.Value <- l path
                with
                | e ->
                    printfn "Error loading %s: %s" path e.Message
                    data.Event.Set())
        | _ -> failwithf "Unknown asset type %s" ext

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
        if not ref.IsReady then failwithf "Error loading asset %s" path
        ref

    // try to reload the asset by path
    member this.TryReload path =
        let mutable data = null
        if database.TryFind(path, &data) then
            this.ForceLoadDataAsync(path, data)

// asset reference
and Ref<'T> internal(path: string, data: Data) =
    [<NonSerialized>]
    let mutable data = data

    // construct asset ref from path; this ref is invalid until fixup
    new (path) = Ref(path, null)

    // is asset loaded?
    member this.IsReady = data.Value <> null

    // wait for asset to be ready
    member this.Wait () = data.Event.Wait()

    // value accessor
    member this.Value = data.Value :?> 'T

    // path accessor
    member this.Path = path

    // asset fixup
    member private this.Fixup ctx =
        // $$$ just make a valid empty ref for now
        data <- Data()
