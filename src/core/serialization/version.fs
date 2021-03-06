module Core.Serialization.Version

open System
open System.Reflection

// initial hash seed for updateHash
let private initialHash = int32 2166136261u

// append a value to the existing hash
let private updateHash ver value =
    (16777619 * ver) ^^^ value

// compute string hash (GetHashCode returns different results on x86/x64)
let private getStringHash (data: string) =
    data |> Seq.fold (fun acc ch -> updateHash acc (int ch)) initialHash

// get hash for enumeration type
let private buildEnumHash (typ: Type) =
    assert typ.IsEnum

    // get all enum values
    let values = Array.zip (typ.GetEnumNames()) [| for v in typ.GetEnumValues() -> hash v |]

    // get enum type hash
    let hash = getStringHash (typ.GetEnumUnderlyingType().Name)

    // return hash combined with value hash
    values
    |> Array.sortBy (fun (_, value) -> value)
    |> Array.fold (fun ver (name, value) ->
        updateHash (updateHash ver value) (getStringHash name)) hash

// a cache of enum hash values
let private enumHashCache = Core.ConcurrentCache(buildEnumHash)

// update hash version with type
let rec private updateVersion toplevel ver (typ: Type) =
    // type name always contributes to the version
    let basever = updateHash ver (getStringHash (string typ))

    // primitive types don't have additional versionable properties
    if typ.IsPrimitive then
        basever
    // enum version depends on enum values
    elif typ.IsEnum then
        updateHash basever (enumHashCache.Get(typ))
    // array version depends on element version
    elif typ.IsArray then
        assert (typ.GetArrayRank() = 1)
        updateVersion false basever (typ.GetElementType())
    // aggregates' version depends on versions of all serializable fields
    elif Util.isStruct typ || (typ.IsClass && toplevel) then
        let fields = Util.getSerializableFields typ

        // accumulate field versions for field static types
        fields
        |> Array.map (fun f -> f.FieldType)
        |> Array.fold (updateVersion false) basever
    // recursing into class types is not necessary because versioning will take place if objects of embedded types are actually serialized
    else
        assert (typ.IsClass || typ.IsInterface)
        basever

// build a full version for type
let private buildVersion (typ: Type) =
    updateVersion true initialHash typ

// a cache of type versions
let private versionCache = Core.ConcurrentCache(buildVersion)

// get type version
let get (typ: Type) =
    versionCache.Get(typ)
