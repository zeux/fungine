module Core.Serialization.Load

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Reflection.Emit

type private ObjectTable = obj array

type private LoadDelegate = delegate of ObjectTable * BinaryReader * obj -> unit

type private LoadMethodHost = class end

let private emitLoadValuePrimitive (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
    objemitpre gen
    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("Read" + typ.Name))
    objemitpost gen

let rec private emitLoadValue (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
    // load primitive types as is
    if typ.IsPrimitive then
        emitLoadValuePrimitive gen objemitpre objemitpost typ
    // load enums as int values
    else if typ.IsEnum then
        emitLoadValuePrimitive gen objemitpre objemitpost typedefof<int>
    // load structs as embedded field lists
    else if typ.IsValueType then
        emitLoadFields gen objemitpre objemitpost typ
    // load objects as object ids
    else
        assert typ.IsClass

        objemitpre gen

        gen.Emit(OpCodes.Ldarg_0) // table
        gen.Emit(OpCodes.Ldarg_1) // reader
        gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("ReadInt32"))
        gen.Emit(OpCodes.Ldelem_Ref)

        objemitpost gen

and private emitLoadFields (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
    let fields = Util.getSerializableFields typ

    // serialize all serializable fields
    for f in fields do
        if Util.isStruct f.FieldType then
            emitLoadValue gen (fun gen -> objemitpre gen; gen.Emit(OpCodes.Ldflda, f)) objemitpost f.FieldType
        else
            emitLoadValue gen objemitpre (fun gen -> gen.Emit(OpCodes.Stfld, f)) f.FieldType

let private emitLoadArray (gen: ILGenerator) objemit (typ: Type) =
    // declare local variables for length and for loop counter
    let idx_local = gen.DeclareLocal(typedefof<int>)
    assert (idx_local.LocalIndex = 0)

    let cnt_local = gen.DeclareLocal(typedefof<int>)
    assert (cnt_local.LocalIndex = 1)

    // store size to local
    objemit gen
    gen.Emit(OpCodes.Ldlen)
    gen.Emit(OpCodes.Stloc_1) // count

    // serialize contents
    let loop_begin = gen.DefineLabel()
    let loop_cmp = gen.DefineLabel()

    gen.Emit(OpCodes.Br, loop_cmp)
    gen.MarkLabel(loop_begin)

    // load element index
    let etype = typ.GetElementType()

    if Util.isStruct etype then
        emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0); gen.Emit(OpCodes.Ldelema, etype)) (fun gen -> ()) etype
    else
        emitLoadValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0)) (fun gen -> gen.Emit(OpCodes.Stelem, etype)) etype

    // index++
    gen.Emit(OpCodes.Ldloc_0) // index
    gen.Emit(OpCodes.Ldc_I4_1)
    gen.Emit(OpCodes.Add)
    gen.Emit(OpCodes.Stloc_0)

    // if (index < count) goto begin
    gen.MarkLabel(loop_cmp)
    gen.Emit(OpCodes.Ldloc_0) // index
    gen.Emit(OpCodes.Ldloc_1) // count
    gen.Emit(OpCodes.Blt, loop_begin)

let private emitLoadByteArray (gen: ILGenerator) objemitpre =
    gen.Emit(OpCodes.Ldarg_1) // reader
    objemitpre gen
    gen.Emit(OpCodes.Ldc_I4_0)
    objemitpre gen
    gen.Emit(OpCodes.Ldlen)
    gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("Read", [|typedefof<byte array>; typedefof<int>; typedefof<int>|]))
    gen.Emit(OpCodes.Pop)

let private emitLoadString (gen: ILGenerator) objemitpre =
    objemitpre gen
    gen.Emit(OpCodes.Ldc_I4_0)
    gen.Emit(OpCodes.Ldarg_1) // reader
    gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("ReadString"))
    gen.Emit(OpCodes.Call, typedefof<string>.GetMethod("FillStringChecked", BindingFlags.Static ||| BindingFlags.NonPublic))

let private emitLoad (gen: ILGenerator) (typ: Type) =
    // deserialize object contents
    if typ.IsValueType then
        emitLoadValue gen (fun gen -> gen.Emit(OpCodes.Ldarg_2); gen.Emit(OpCodes.Unbox, typ)) (fun gen -> gen.Emit(OpCodes.Stobj, typ)) typ
    else if typ = typedefof<byte array> then
        emitLoadByteArray gen (fun gen -> gen.Emit(OpCodes.Ldarg_2))
    else if typ = typedefof<string> then
        emitLoadString gen (fun gen -> gen.Emit(OpCodes.Ldarg_2))
    else if typ.IsArray then
        assert (typ.GetArrayRank() = 1)
        emitLoadArray gen (fun gen -> gen.Emit(OpCodes.Ldarg_2)) typ
    else
        emitLoadFields gen (fun gen -> gen.Emit(OpCodes.Ldarg_2)) (fun gen -> ()) typ

    gen.Emit(OpCodes.Ret)

let private buildLoadDelegate (typ: Type) =
    let dm = DynamicMethod(typ.ToString(), null, [|typedefof<ObjectTable>; typedefof<BinaryReader>; typedefof<obj>|], typedefof<LoadMethodHost>, true)
    let gen = dm.GetILGenerator()

    emitLoad gen typ

    dm.CreateDelegate(typedefof<LoadDelegate>) :?> LoadDelegate

let private loadDelegateCache = Dictionary<Type, LoadDelegate>()

let private getLoadDelegate typ =
    match loadDelegateCache.TryGetValue(typ) with
    | true, value -> value
    | _ ->
        let d = buildLoadDelegate typ
        loadDelegateCache.Add(typ, d)
        d

let fromStream stream =
    let reader = new BinaryReader(stream)

    // read header
    let signature = reader.ReadString()

    if signature <> "fun" then failwith "Incorrect header"

    // read type table
    let type_names = Array.init (reader.ReadInt32()) (fun _ -> reader.ReadString())

    // resolve types
    let types = type_names |> Array.map (fun name -> Type.GetType(name))

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
        let d = getLoadDelegate (obj.GetType())

        d.Invoke(table, reader, obj))
        
    objects.[0]

let fromFile path =
    use stream = new FileStream(path, FileMode.Open)
    fromStream stream
