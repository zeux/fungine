namespace BuildSystem

open System
open System.IO

// node object (represents a file or pseudo-file)
[<Struct>]
type Node private(path: string, uid: string) =
    // root path (used for normalization)
    static let mutable root = ""
    static let normalize path = Path.GetFullPath(path).Replace('\\', '/')

    // get node from path
    new (path) =
        // get root-relative path
        let relpath =
            let full = normalize path

            // optimization (this is faster than MakeRelativeUri)
            if full.StartsWith(root, StringComparison.InvariantCultureIgnoreCase) then
                full.Substring(root.Length)
            else
                Uri(root).MakeRelativeUri(Uri(full)).OriginalString

        // construct node
        Node(relpath, relpath.ToLowerInvariant())

    // unique id
    member this.Uid = uid

    // original path
    member this.Path = path

    // file info
    member this.Info = FileInfo(path)

    // string conversion
    override this.ToString() = path

    // root path accessor
    static member Root
        with get () = root
        and set value = root <- normalize value + "/"