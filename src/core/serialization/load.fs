module Core.Serialization.Load

open System
open System.IO
open System.Reflection
open System.Reflection.Emit

// a table that holds all objects in a loaded graph
type private ObjectTable = obj array

// a delegate for loading objects; there is an instance of one for each type
type private LoadDelegate = delegate of ObjectTable * BinaryReader * obj -> unit

// all load methods are created as methods of this type
type private LoadMethodHost = class end

// dummy function for callbacks
let private emitNone gen = ()

// load any value for which there is a BinaryWriter.ReadType method (all primitive types)
let private emitLoadValuePrimitive (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
    objemitpre gen
    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("Read" + typ.Name))
    objemitpost gen

// load an object
let private emitLoadObject (gen: ILGenerator) objemitpre objemitpost =
    objemitpre gen

    // read id and fetch object from object table
    gen.Emit(OpCodes.Ldarg_0) // table
    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("ReadInt32"))
    gen.Emit(OpCodes.Ldelem_Ref)

    objemitpost gen

// load any value (dispatcher function)
let rec private emitLoadValue (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
    // load primitive types as is
    if typ.IsPrimitive then
        emitLoadValuePrimitive gen objemitpre objemitpost typ
    // load enums as integer values
    else if typ.IsEnum then
        emitLoadValuePrimitive gen objemitpre objemitpost (typ.GetEnumUnderlyingType())
    // load structs as embedded field lists
    else if typ.IsValueType then
        emitLoadFields gen objemitpre typ
    // load objects as object ids
    else
        assert typ.IsClass
        emitLoadObject gen objemitpre objemitpost

// load all fields of a class/struct
and private emitLoadFields (gen: ILGenerator) objemit (typ: Type) =
    let fields = Util.getSerializableFields typ

    // serialize all serializable fields
    for f in fields do
        if Util.isStruct f.FieldType then
            emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldflda, f)) emitNone f.FieldType
        else
            emitLoadValue gen objemit (fun gen -> gen.Emit(OpCodes.Stfld, f)) f.FieldType

// load an array
let private emitLoadArray (gen: ILGenerator) objemit (typ: Type) =
    // serialize contents
    let etype = typ.GetElementType()

    Util.emitArrayLoop gen objemit (fun gen ->
        if Util.isStruct etype then
            emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0); gen.Emit(OpCodes.Ldelema, etype)) emitNone etype
        else
            emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0)) (fun gen -> gen.Emit(OpCodes.Stelem, etype)) etype)

// load byte array (fast path)
let private emitLoadByteArray (gen: ILGenerator) objemit =
    gen.Emit(OpCodes.Ldarg_1) // reader
    objemit gen
    gen.Emit(OpCodes.Ldc_I4_0)
    objemit gen
    gen.Emit(OpCodes.Ldlen)
    gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("Read", [|typedefof<byte array>; typedefof<int>; typedefof<int>|]))
    gen.Emit(OpCodes.Pop)

// load string
let private emitLoadString (gen: ILGenerator) objemit =
    objemit gen
    gen.Emit(OpCodes.Ldc_I4_0)
    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("ReadString"))
    gen.Emit(OpCodes.Call, typedefof<string>.GetMethod("FillStringChecked", BindingFlags.Static ||| BindingFlags.NonPublic))

// load a top-level type
let private emitLoad (gen: ILGenerator) (typ: Type) =
    // deserialize object contents
    let objemit (gen: ILGenerator) = gen.Emit(OpCodes.Ldarg_2)

    if typ.IsValueType then
        emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Unbox, typ)) (fun gen -> gen.Emit(OpCodes.Stobj, typ)) typ
    else if typ = typedefof<byte array> then
        emitLoadByteArray gen objemit
    else if typ = typedefof<string> then
        emitLoadString gen objemit
    else if typ.IsArray then
        assert (typ.GetArrayRank() = 1)
        emitLoadArray gen objemit typ
    else
        emitLoadFields gen objemit typ

    gen.Emit(OpCodes.Ret)

// create a load delegate for a given type
let private buildLoadDelegate (typ: Type) =
    let dm = DynamicMethod(typ.ToString(), null, [|typedefof<ObjectTable>; typedefof<BinaryReader>; typedefof<obj>|], typedefof<LoadMethodHost>, true)
    let gen = dm.GetILGenerator()

    emitLoad gen typ

    dm.CreateDelegate(typedefof<LoadDelegate>) :?> LoadDelegate

// a cache for save delegates (one delegate per type)
let private loadDelegateCache = Util.TypeCache(buildLoadDelegate)

// load object from stream
let fromStream stream =
    let reader = new BinaryReader(stream)

    // read header
    let signature = reader.ReadString()

    if signature <> "fun" then failwith "Incorrect header"

    // read type table
    let type_count = reader.ReadInt32()
    let type_names = Array.init type_count (fun _ -> reader.ReadString())
    let type_versions = Array.init type_count (fun _ -> reader.ReadUInt32())

    // resolve types
    let types =
        Array.map2 (fun name version ->
            let typ = Type.GetType(name)
            if version <> Version.get typ then failwithf "Version mismatch for type %A" typ
            typ) type_names type_versions

    // read object table
    let object_types = Array.init (reader.ReadInt32()) (fun _ -> types.[reader.ReadInt32()])

    // read array size table
    let array_size_indices = object_types |> Array.map (fun typ -> if typ.IsArray || typ = typedefof<string> then 1 else 0) |> Array.scan (+) 0
    assert (array_size_indices.Length = object_types.Length + 1)

    let array_sizes = Array.init array_size_indices.[object_types.Length] (fun _ -> reader.ReadInt32())

    // create uninitialized objects
    let objects = object_types |> Array.mapi (fun idx typ ->
        if typ.IsArray then
            assert (typ.GetArrayRank() = 1)

            let length = array_sizes.[array_size_indices.[idx]]
            System.Array.CreateInstance(typ.GetElementType(), length) :> obj
        else if typ = typedefof<string> then
            let length = array_sizes.[array_size_indices.[idx]]
            typ.GetMethod("FastAllocateString", BindingFlags.Static ||| BindingFlags.NonPublic).Invoke(null, [|box length|])
        else
            System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typ))

    // create object table (0 is the null object, object indices are 1-based)
    let table = Array.append [|null|] objects

    // load objects
    objects |> Array.iter (fun obj ->
        let d = loadDelegateCache.Get(obj.GetType())

        d.Invoke(table, reader, obj))
        
    objects.[0]

// load object from file
let fromFile path =
    use stream = new FileStream(path, FileMode.Open)
    fromStream stream