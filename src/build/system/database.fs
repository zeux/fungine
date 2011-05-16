namespace BuildSystem

open System.IO
open System.Collections.Concurrent
open System.Collections.Generic

// content signature; file contents is rescanned on metadata change
[<Struct>]
type ContentSignature(size: int64, time: int64, csig: Signature) =
    // ctor to compute signature from scratch
    new (info: FileInfo) =
        let csig = Signature.FromFile(info.FullName)
        ContentSignature(info.Length, info.LastWriteTimeUtc.ToFileTimeUtc(), csig)

    // file size
    member this.Size = size

    // file modification time
    member this.Time = time

    // file contents signature
    member this.Signature = csig

// task signature; stores information about last successful task build
[<Struct>]
type TaskSignature(inputs: (string * Signature) array) =
    // task inputs (sources and explicit dependencies)
    member this.Inputs = inputs

// storage helper for database (can't use database directly because serialization does not support .NET serialization callbacks)
type private DatabaseStorage =
    { csigs: KeyValuePair<string, ContentSignature> array
      tsigs: KeyValuePair<string, TaskSignature> array }

// persistent storage of build information
type Database(path) =
    let csigs = ConcurrentDictionary<string, ContentSignature>()
    let tsigs = ConcurrentDictionary<string, TaskSignature>()

    // load from file
    do
        try
            let storage = Core.Serialization.Load.fromFile path :?> DatabaseStorage
            for s in storage.csigs do csigs.TryAdd(s.Key, s.Value) |> ignore
            for s in storage.tsigs do tsigs.TryAdd(s.Key, s.Value) |> ignore
        with
        | e ->
            Output.echof "*** warning: database load error: %s ***" e.Message

    // save to file
    member this.Flush () =
        let storage = { new DatabaseStorage with csigs = csigs.ToArray() and tsigs = tsigs.ToArray() }
        Core.Serialization.Save.toFile path storage

    // get content signature, or construct new one
    member this.ContentSignature (node: Node) =
        let info = node.Info

        // perform cache lookup & check metadata validity
        match csigs.TryGetValue(node.Uid) with
        | true, csig when csig.Size = info.Length && csig.Time = info.LastWriteTimeUtc.ToFileTimeUtc() ->
            csig.Signature
        | _ ->
            // build new signature
            let s = csigs.AddOrUpdate(node.Uid, (fun _ -> ContentSignature(info)), (fun _ _ -> ContentSignature(info)))
            Output.debug Output.Options.DebugFileSignature (fun e -> e "%s -> %A" node.Uid s.Signature)
            s.Signature

    // get/set task signature
    member this.TaskSignature
        with get uid =
            match tsigs.TryGetValue(uid) with
            | true, s -> Some s
            | _ -> None
        and set uid value =
            tsigs.[uid] <- value