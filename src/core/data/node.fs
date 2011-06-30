namespace Core.Data

open System.Collections.Generic

// node location in file, used for debugging & error messages
[<Struct>]
type Location(path: string, line: int) =
    // location path
    member this.Path = path

    // location line
    member this.Line = line

    // location debug print
    override this.ToString () = sprintf "%s(%d)" path line

// node representation
type Node =
| Value of string
| Array of Node array
| Object of (string * Node) array

// document representation
type Document(root: Node, locations: IDictionary<Node, Location>) =
    // document root
    member this.Root = root

    // get node location, if any
    member this.Location node =
        match locations.TryGetValue(node) with
        | true, value -> value
        | _ -> Location()

