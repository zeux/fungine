namespace BuildSystem

open System
open System.IO

// node object (represents a file or pseudo-file)
[<Struct>]
type Node private(path: string, uid: string) =
    // root path (used for normalization)
    static let mutable root = ""
    static let normalize path = Path.GetFullPath(path).ToLowerInvariant().Replace('\\', '/')

    // get node from path
    new (path) =
        Node(path,
            let full = normalize path
            if full.StartsWith(root) then
                full.Substring(root.Length)
            else
                Uri(root).MakeRelativeUri(Uri(full)).OriginalString)

    // unique id
    member this.Uid = uid

    // original path
    member this.Path = path

    // file info
    member this.Info = FileInfo(path)

    // root path accessor
    static member Root
        with get () = root
        and set value = root <- normalize value + "/"