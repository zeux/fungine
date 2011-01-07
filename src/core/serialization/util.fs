module Core.Serialization.Util

open System
open System.Collections.Generic
open System.Reflection

// a cache for delegates based on types
type DelegateCache<'a>(creator) =
    let cache = Dictionary<Type, 'a>()

    // get the value from the cache, creating it as necessary
    member x.Get typ =
        match cache.TryGetValue(typ) with
        | true, value -> value
        | _ ->
            let d = creator typ
            cache.Add(typ, d)
            d

// return true if type is a struct
let isStruct (typ: Type) =
    typ.IsValueType && not typ.IsPrimitive && not typ.IsEnum

// get all fields from the type and its ancestors that are eligible for serialization
let getSerializableFields (typ: Type) =
    if not typ.IsSerializable then failwith (sprintf "Type %A is not serializable" typ)

    // get all fields
    let fields = typ.FindMembers(MemberTypes.Field, BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public, null, null) |> Array.map (fun f -> f :?> FieldInfo)

    // get all serializable fields
    let fields_ser = fields |> Array.filter (fun f -> not f.IsNotSerialized)

    fields_ser