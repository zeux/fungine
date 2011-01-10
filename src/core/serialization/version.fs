module Core.Serialization.Version

open System
open System.Reflection

// initial hash seed for updateHash
let private initialHash = 2166136261u

// append a value to the existing hash
let private updateHash ver value =
    (16777619u * ver) ^^^ value

// get hash for enumeration type
let private buildEnumHash (typ: Type) =
    assert typ.IsEnum

    // get all enum values
    let values = Array.zip (typ.GetEnumNames()) [| for v in typ.GetEnumValues() -> v :?> int |]

    // get enum type hash
    let hash = uint32 (typ.GetEnumUnderlyingType().GetHashCode())

    // return hash combined with value hash
    values
    |> Array.sortBy (fun (_, value) -> value)
    |> Array.fold (fun ver (name, value) ->
        updateHash (updateHash ver (uint32 value)) (uint32 (name.GetHashCode()))) hash

// a cache of enum hash values
let private enumHashCache = Util.TypeCache(buildEnumHash)

// update hash version with type
let rec private updateVersion ver (typ: Type) =
    // struct version depends on versions of all serializable fields
    if Util.isStruct typ then
        let fields = Util.getSerializableFields typ

        // accumulate field versions for field static types
        fields
        |> Array.map (fun f -> f.FieldType)
        |> Array.fold updateVersion ver
    // enum version depends on enum values
    else if typ.IsEnum then
        updateHash ver (enumHashCache.Get(typ))
    // other types' versions are just the names
    else
        assert (typ.IsPrimitive || typ.IsClass)
        updateHash ver (uint32 (typ.FullName.GetHashCode()))

// build a full version for type
let private buildVersion (typ: Type) =
    updateVersion initialHash typ

// a cache of type versions
let private versionCache = Util.TypeCache<_>(buildVersion)

// get type version
let get (typ: Type) =
    versionCache.Get(typ)