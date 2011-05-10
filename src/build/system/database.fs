namespace BuildSystem

open System.IO
open System.Collections.Generic

// content signature; file contents is rescanned on metadata change, so this 
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
type TaskSignature(inputs: (string * Signature) array, outputs: string array) =
    // complete task signature; is used as unique task output contents identifier for file cache
    member this.Signature =
        [| inputs |> Array.collect (fun (path, csig) -> [| Signature.FromString(path); csig |])
           outputs |> Array.map Signature.FromString |]
        |> Array.concat
        |> Signature.Combine

// persistent storage of build information
type Database() =
    let csigs = Dictionary<string, ContentSignature>()
    let tsigs = Dictionary<string, TaskSignature>()

    // get content signature, or construct new one
    member this.ContentSignature path =
        let info = FileInfo(path)

        // perform cache lookup & check metadata validity
        match csigs.TryGetValue(path) with
        | true, csig when csig.Size = info.Length && csig.Time = info.LastWriteTimeUtc.ToFileTimeUtc() ->
            csig.Signature
        | _ ->
            // build new signature
            let s = ContentSignature(info)
            csigs.[path] <- s
            s.Signature

    // get/set task signature
    member this.TaskSignature
        with get uid =
            match tsigs.TryGetValue(uid) with
            | true, s -> Some s
            | _ -> None
        and set uid value =
            tsigs.[uid] <- value