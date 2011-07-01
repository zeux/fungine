module Core.Data.Read

open System
open System.Reflection
open System.Runtime.Serialization

// read string from node
let private readString node =
    match node with
    | Value v -> v
    | _ -> failwith "expected a value"

// read enum from node
let private readEnum (typ: Type) node =
    let v = readString node
    let names = typ.GetEnumNames()
    match names |> Array.tryFindIndex ((=) v) with
    | Some i -> typ.GetEnumValues().GetValue(i)
    | None -> failwithf "unknown value %s (expected one of %s)" v (names |> String.concat ", ")
    
// read primitive type from node
let private readPrimitive (typ: Type) node =
    let v = readString node
    let m = typ.GetMethod("Parse", [| typedefof<string> |])
    m.Invoke(null, [|v|])

// get value parser for a type
let private getValueParser (typ: Type) =
    if typ.IsEnum then 
        Some (readEnum typ)
    else if typ.IsPrimitive then
        Some (readPrimitive typ)
    else if typ = typedefof<string> then
        Some (readString >> box)
    else
        None

// extract value setters/parsers from a type
let private getValueSetters (typ: Type) =
    // get field/property setters
    [|
        for f in typ.GetFields() -> f.Name, f.FieldType, fun obj value -> f.SetValue(obj, value)
        for p in typ.GetProperties() do if p.CanWrite then yield p.Name, p.PropertyType, fun obj value -> p.SetValue(obj, value, null)
    |]
    // construct special setters for option types
    |> Array.map (fun (name, typ, setter) ->
        if typ.Name = "FSharpOption`1" then
            let some = typ.GetMethod("Some")
            (name, typ.GetGenericArguments().[0], fun obj value -> setter obj (some.Invoke(null, [|value|])))
        else
            (name, typ, setter))
    // get value parsers for types
    |> Array.choose (fun (name, typ, setter) ->
        match getValueParser typ with
        | Some parser -> Some (name, fun (obj: obj) (doc: Document) node ->
            try
                setter obj (parser node)
            with
            | e -> failwithf "%A: value parsing failed: %s" (doc.Location node) e.Message)
        | None -> None)
    |> dict

// value setters cache
let private valueSetters = Core.ConcurrentCache(getValueSetters)

// read data from node into an object
let readNode<'T> (doc: Document) node =
    // construct an empty object
    let typ = typedefof<'T>
    let obj = FormatterServices.GetUninitializedObject(typ)

    // read all fields
    match node with
    | Object elements ->
        let setters = valueSetters.Get typ

        for (name, data) in elements do
            match setters.TryGetValue(name) with
            | true, setter -> setter obj doc data
            | _ -> failwithf "%A: unknown key %s" (doc.Location data) name
    | _ -> failwithf "%A: expected an object" (doc.Location node)

    obj :?> 'T