module Core.Serialization.Load

open System
open System.IO
open System.IO.MemoryMappedFiles
open System.Reflection
open System.Reflection.Emit
open System.Runtime.InteropServices

open Microsoft.FSharp.NativeInterop

#nowarn "9" // Uses of this construct may result in the generation of unverifiable .NET IL code

// a memory chunk-based reader
type MemoryReader(buffer: nativeptr<byte>) =
    let mutable data = NativePtr.toNativeInt buffer

    // get current data pointer
    member this.Data = data

    // read struct value and advance current pointer
    member this.ReadValue<'T when 'T: unmanaged>() =
        let r: 'T = NativePtr.read (NativePtr.ofNativeInt data)
        data <- data + (nativeint sizeof<'T>)
        r

    // read int32 and advance current pointer
    member this.ReadInt32(): int32 = this.ReadValue()

    // read int32 encoded in 7-bit chunks (this matches the BinaryReader/BinaryWriter string length encoding)
    member this.Read7BitEncodedInt() =
        let rec loop acc shift =
            match this.ReadValue() with
            | x when x < 128uy -> acc ||| (int x <<< shift)
            | x -> loop (acc ||| (int (x - 128uy) <<< shift)) (shift + 7)

        loop 0 0

    // read string and advance current pointer
    member this.ReadString() =
        let length = this.Read7BitEncodedInt()
        let result = String(((NativePtr.ofNativeInt data): nativeptr<sbyte>), 0, length, Util.stringEncoding)
        data <- data + nativeint length
        result

    // read string into a preallocated native buffer and advance current pointer
    member this.ReadStringData(result: nativeptr<char>, size: int) =
        let length = this.Read7BitEncodedInt()

        let decoded = Util.stringEncoding.GetChars(NativePtr.ofNativeInt data, length, result, size)
        assert (decoded = size)

        data <- data + nativeint length

// a table that holds all objects in a loaded graph
type private ObjectTable = obj array

// a delegate for creating objects; there is an instance of one for each type
type private CreateDelegate = delegate of int -> obj

// a delegate for loading objects; there is an instance of one for each type
type private LoadDelegate = delegate of ObjectTable * MemoryReader * obj -> unit

// all load methods are created as methods of this type
type private LoadMethodHost = class end

// dummy function for callbacks
let private emitNone gen = ()

// load any value for which there is a BinaryWriter.ReadType method (all primitive types)
let private emitLoadValuePrimitive (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
    objemitpre gen
    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Call, typedefof<MemoryReader>.GetMethod("ReadValue").MakeGenericMethod([|typ|]))
    objemitpost gen

// load an object
let private emitLoadObject (gen: ILGenerator) objemitpre objemitpost =
    objemitpre gen

    // read id and fetch object from object table
    gen.Emit(OpCodes.Ldarg_0) // table
    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Call, typedefof<MemoryReader>.GetMethod("ReadInt32"))
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

// load array (fast path)
let private emitLoadPrimitiveArray (gen: ILGenerator) objemit (typ: Type) =
    let data_field = typedefof<MemoryReader>.GetField("data", BindingFlags.Instance ||| BindingFlags.NonPublic)
    let size_local = gen.DeclareLocal(typedefof<int>)

    // calculate array size in bytes
    objemit gen
    gen.Emit(OpCodes.Ldlen)
    gen.Emit(OpCodes.Sizeof, typ.GetElementType())
    gen.Emit(OpCodes.Mul)
    gen.Emit(OpCodes.Stloc, size_local)

    // copy data
    objemit gen
    gen.Emit(OpCodes.Ldc_I4_0)
    gen.Emit(OpCodes.Ldelema, typ.GetElementType())

    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Ldfld, data_field)

    gen.Emit(OpCodes.Ldloc, size_local)

    gen.Emit(OpCodes.Unaligned, 1uy)
    gen.Emit(OpCodes.Cpblk)

    // increase reader pointer
    gen.Emit(OpCodes.Ldarg_1)

    gen.Emit(OpCodes.Ldarg_1)
    gen.Emit(OpCodes.Ldfld, data_field)
    gen.Emit(OpCodes.Ldloc, size_local)
    gen.Emit(OpCodes.Add)

    gen.Emit(OpCodes.Stfld, data_field)

// load an array
let private emitLoadArray (gen: ILGenerator) objemit (typ: Type) =
    let etype = typ.GetElementType()

    // serialize contents
    Util.emitArrayLoop gen objemit (fun gen ->
        if Util.isStruct etype then
            emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0); gen.Emit(OpCodes.Ldelema, etype)) emitNone etype
        else
            emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0)) (fun gen -> gen.Emit(OpCodes.Stelem, etype)) etype)

// load string
let private emitLoadString (gen: ILGenerator) objemit =
    let data_field = typedefof<MemoryReader>.GetField("data", BindingFlags.Instance ||| BindingFlags.NonPublic)
    let char_local = gen.DeclareLocal(typedefof<nativeint>, pinned = true)

    // get pointer to string data
    objemit gen
    gen.Emit(OpCodes.Ldflda, typedefof<string>.GetField("m_firstChar", BindingFlags.Instance ||| BindingFlags.NonPublic))
    gen.Emit(OpCodes.Stloc, char_local)

    // load string data
    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Ldloc, char_local)
    objemit gen
    gen.Emit(OpCodes.Ldfld, typedefof<string>.GetField("m_stringLength", BindingFlags.Instance ||| BindingFlags.NonPublic))
    gen.Emit(OpCodes.Call, typedefof<MemoryReader>.GetMethod("ReadStringData"))

// load a top-level type
let private emitLoad (gen: ILGenerator) (typ: Type) =
    // deserialize object contents
    let objemit (gen: ILGenerator) = gen.Emit(OpCodes.Ldarg_2)

    if typ.IsValueType then
        emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Unbox, typ)) (fun gen -> gen.Emit(OpCodes.Stobj, typ)) typ
    else if typ = typedefof<string> then
        emitLoadString gen objemit
    else if typ.IsArray then
        assert (typ.GetArrayRank() = 1)
        (if typ.GetElementType().IsPrimitive then emitLoadPrimitiveArray else emitLoadArray) gen objemit typ
    else
        emitLoadFields gen objemit typ

    gen.Emit(OpCodes.Ret)

// create a load delegate for a given type
let private buildLoadDelegate (typ: Type) =
    let dm = DynamicMethod("load " + typ.ToString(), null, [|typedefof<ObjectTable>; typedefof<MemoryReader>; typedefof<obj>|], typedefof<LoadMethodHost>, true)
    let gen = dm.GetILGenerator()

    emitLoad gen typ

    dm.CreateDelegate(typedefof<LoadDelegate>) :?> LoadDelegate

// a cache for load delegates (one delegate per type)
let private loadDelegateCache = Util.TypeCache(buildLoadDelegate)

// create a create delegate for a given type
let private buildCreateDelegate (typ: Type) =
    let dm = DynamicMethod("create " + typ.ToString(), typedefof<obj>, [|typedefof<int>|], typedefof<LoadMethodHost>, true)
    let gen = dm.GetILGenerator()

    if typ.IsArray then
        assert (typ.GetArrayRank() = 1)

        gen.Emit(OpCodes.Ldarg_0)
        gen.Emit(OpCodes.Newarr, typ.GetElementType())
    else if typ = typedefof<string> then
        gen.Emit(OpCodes.Ldarg_0)
        gen.Emit(OpCodes.Call, typ.GetMethod("FastAllocateString", BindingFlags.Static ||| BindingFlags.NonPublic))
    else
        gen.Emit(OpCodes.Ldtoken, typ)
        gen.Emit(OpCodes.Call, typedefof<Type>.GetMethod("GetTypeFromHandle"))
        gen.Emit(OpCodes.Call, typedefof<System.Runtime.Serialization.FormatterServices>.GetMethod("GetUninitializedObject"))

    gen.Emit(OpCodes.Ret)

    dm.CreateDelegate(typedefof<CreateDelegate>) :?> CreateDelegate

// a cache for create delegates (one delegate per type)
let private createDelegateCache = Util.TypeCache(buildCreateDelegate)

// load object from memory
let fromMemory data size =
    let reader = MemoryReader(data)

    // read header
    let signature = reader.ReadString()

    if signature <> "fun" then failwith "Incorrect header"

    // read type table
    let type_count = reader.ReadInt32()
    let type_names = Array.init type_count (fun _ -> reader.ReadString())
    let type_versions = Array.init type_count (fun _ -> reader.ReadInt32())

    // resolve types
    let types =
        Array.map2 (fun name version ->
            let typ = Type.GetType(name)
            if version <> Version.get typ then failwithf "Version mismatch for type %A" typ
            typ) type_names type_versions

    // get delegates
    let creators = types |> Array.map createDelegateCache.Get
    let loaders = types |> Array.map loadDelegateCache.Get
    let needs_array = types |> Array.map (fun typ -> typ.IsArray || typ = typedefof<string>)

    // read object table
    let object_types = Array.init (reader.ReadInt32()) (fun _ -> reader.ReadInt32())

    // read array size table
    let array_size_indices = object_types |> Array.map (fun tid -> if needs_array.[tid] then 1 else 0) |> Array.scan (+) 0
    assert (array_size_indices.Length = object_types.Length + 1)

    let array_sizes = Array.init array_size_indices.[object_types.Length] (fun _ -> reader.ReadInt32())

    // create uninitialized objects
    let objects = object_types |> Array.mapi (fun idx tid ->
        let c = creators.[tid]

        c.Invoke(if needs_array.[tid] then array_sizes.[array_size_indices.[idx]] else 0))

    // create object table (0 is the null object, object indices are 1-based)
    let table = Array.append [|null|] objects

    // load objects
    objects |> Array.iteri (fun idx obj ->
        let d = loaders.[object_types.[idx]]

        d.Invoke(table, reader, obj))

    // check bounds
    assert (reader.Data <= (NativePtr.toNativeInt data) + (nativeint size))

    objects.[0]

// load object from stream
let fromStream (stream: Stream) size =
    let data = Array.zeroCreate size

    let read = stream.Read(data, 0, size)
    assert (read = size)
    
    let gch = GCHandle.Alloc(data, GCHandleType.Pinned) 

    try
        fromMemory (gch.AddrOfPinnedObject() |> NativePtr.ofNativeInt) size
    finally
        gch.Free()

// load object from file
let fromFile path =
    use file = MemoryMappedFile.CreateFromFile(path)
    use stream = file.CreateViewStream(0L, 0L, MemoryMappedFileAccess.Read)

    let handle = stream.SafeMemoryMappedViewHandle
    let data = ref (NativePtr.ofNativeInt (nativeint 0))
    handle.AcquirePointer(data)

    try
        fromMemory !data (int stream.Length)
    finally
        handle.ReleasePointer()