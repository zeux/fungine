namespace Core.Serialization

type Fixup =
    // pack several fixup contexts into an array of items
    static member Create (a) = [|box a|]
    static member Create (a, b) = [|box a; box b|]
    static member Create (a, b, c) = [|box a; box b; box c|]
    static member Create (a, b, c, d) = [|box a; box b; box c; box d|]
    static member Create (a, b, c, d, e) = [|box a; box b; box c; box d; box e|]

    // unpack value of specific type from fixup context array
    static member Get<'T> (arr: obj array) =
        arr |> Array.find (function :? 'T -> true | _ -> false) :?> 'T