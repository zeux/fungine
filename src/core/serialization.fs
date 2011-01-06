module Core.Serialization

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Reflection.Emit

let isStruct (typ: Type) =
    typ.IsValueType && not typ.IsPrimitive && not typ.IsEnum

let getSerializableFields (typ: Type) =
    if not typ.IsSerializable then failwith (sprintf "Type %A is not serializable" typ)

    // get all fields
    let fields = typ.FindMembers(MemberTypes.Field, BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public, null, null) |> Array.map (fun f -> f :?> FieldInfo)

    // get all serializable fields
    let fields_ser = fields |> Array.filter (fun f -> not f.IsNotSerialized)

    fields_ser

module Loader =
    type ObjectTable = obj array

    type LoadDelegate = delegate of ObjectTable * BinaryReader * obj -> unit

    type LoadMethodHost = class end

    let emitLoadValuePrimitive (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
        objemitpre gen
        gen.Emit(OpCodes.Ldarg_1) // reader
        gen.Emit(OpCodes.Call, typedefof<BinaryReader>.GetMethod("Read" + typ.Name))
        objemitpost gen

    let rec emitLoadValue (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
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

    and emitLoadFields (gen: ILGenerator) objemitpre objemitpost (typ: Type) =
        let fields = getSerializableFields typ

        // serialize all serializable fields
        for f in fields do
            if isStruct f.FieldType then
                emitLoadValue gen (fun gen -> objemitpre gen; gen.Emit(OpCodes.Ldflda, f)) objemitpost f.FieldType
            else
                emitLoadValue gen objemitpre (fun gen -> gen.Emit(OpCodes.Stfld, f)) f.FieldType

    let emitLoadArray (gen: ILGenerator) objemit (typ: Type) =
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

        if isStruct etype then
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

    let emitLoad (gen: ILGenerator) (typ: Type) =
        // deserialize object contents
        if typ.IsValueType then
            ()
            // emitLoadValue gen (fun gen -> gen.Emit(OpCodes.Ldarg_2); gen.Emit(OpCodes.Unbox_Any, typ)) typ
        else
            if typ = typedefof<string> || typ = typedefof<byte array> || typ = typedefof<char array> then
                emitLoadValuePrimitive gen (fun gen -> gen.Emit(OpCodes.Ldarg_2)) (fun gen -> ()) typ
            else if typ.IsArray then
                assert (typ.GetArrayRank() = 1)
                emitLoadArray gen (fun gen -> gen.Emit(OpCodes.Ldarg_2)) typ
            else
                emitLoadFields gen (fun gen -> gen.Emit(OpCodes.Ldarg_2)) (fun gen -> ()) typ

        gen.Emit(OpCodes.Ret)

    let buildLoadDelegate (typ: Type) =
        let dm = DynamicMethod(typ.ToString(), null, [|typedefof<ObjectTable>; typedefof<BinaryReader>; typedefof<obj>|], typedefof<LoadMethodHost>, true)
        let gen = dm.GetILGenerator()

        emitLoad gen typ

        dm.CreateDelegate(typedefof<LoadDelegate>) :?> LoadDelegate

    let loadDelegateCache = Dictionary<Type, LoadDelegate>()

    let getLoadDelegate typ =
        match loadDelegateCache.TryGetValue(typ) with
        | true, value -> value
        | _ ->
            let d = buildLoadDelegate typ
            loadDelegateCache.Add(typ, d)
            d

let loadFromStream stream =
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
    let array_size_indices = object_types |> Array.map (fun typ -> if typ.IsArray then 1 else 0) |> Array.scan (+) 0
    assert (array_size_indices.Length = object_types.Length + 1)

    let array_sizes = Array.init array_size_indices.[object_types.Length] (fun _ -> reader.ReadInt32())

    // create uninitialized objects
    let objects = object_types |> Array.mapi (fun idx typ ->
        if typ.IsArray then
            assert (typ.GetArrayRank() = 1)

            let length = array_sizes.[array_size_indices.[idx]]
            System.Array.CreateInstance(typ.GetElementType(), length) :> obj
        else
            System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typ))

    // create object table (0 is the null object, object indices are 1-based)
    let table = Array.append [|null|] objects

    // load objects
    objects |> Array.iter (fun obj ->
        let d = Loader.getLoadDelegate (obj.GetType())

        d.Invoke(table, reader, obj))
        
    objects.[0]

let loadFromFile path =
    use stream = new FileStream(path, FileMode.Open)
    loadFromStream stream

module Saver =
    type ObjectTable() =
        let object_queue = Queue<obj>()
        let object_ids = Dictionary<obj, int>(HashIdentity.Reference)

        // get the list of objects to be serialized
        member x.PendingObjects = seq { while object_queue.Count > 0 do yield object_queue.Dequeue() }

        // get the list of all objects
        member x.Objects = object_ids |> Array.ofSeq

        // get a serialized id of the object
        member x.Object (obj: obj) =
            // null is encoded with 0
            if Object.ReferenceEquals(obj, null) then
                0
            else
                // non-null objects are encoded with 1-based object index in the object table
                1 +
                match object_ids.TryGetValue(obj) with
                | true, id -> id
                | _ ->
                    let id = object_ids.Count
                    object_ids.Add(obj, id)
                    object_queue.Enqueue(obj)
                    id

    type SaveDelegate = delegate of ObjectTable * BinaryWriter * obj -> unit

    type SaveMethodHost = class end

    let emitSaveValuePrimitive (gen: ILGenerator) objemit typ =
        gen.Emit(OpCodes.Ldarg_1) // writer
        objemit gen
        gen.Emit(OpCodes.Call, typedefof<BinaryWriter>.GetMethod("Write", [|typ|]))

    let rec emitSaveValue (gen: ILGenerator) objemit (typ: Type) =
        // save primitive types as is
        if typ.IsPrimitive then
            emitSaveValuePrimitive gen objemit typ
        // save enums as int values
        else if typ.IsEnum then
            emitSaveValuePrimitive gen objemit typedefof<int>
        // save structs as embedded field lists
        else if typ.IsValueType then
            emitSaveFields gen objemit typ
        // save objects as object ids (defer actual saving)
        else
            assert typ.IsClass
            gen.Emit(OpCodes.Ldarg_1) // writer

            // push object id
            gen.Emit(OpCodes.Ldarg_0) // table
            objemit gen
            gen.Emit(OpCodes.Call, typedefof<ObjectTable>.GetMethod("Object"))

            // write object id
            gen.Emit(OpCodes.Call, typedefof<BinaryWriter>.GetMethod("Write", [|typedefof<int>|]))

    and emitSaveFields (gen: ILGenerator) objemit (typ: Type) =
        let fields = getSerializableFields typ

        // serialize all serializable fields
        for f in fields do
            // load structs by reference to conserve stack space
            let cmd = if isStruct f.FieldType then OpCodes.Ldflda else OpCodes.Ldfld

            emitSaveValue gen (fun gen -> objemit gen; gen.Emit(cmd, f)) f.FieldType

    let emitSaveArray (gen: ILGenerator) objemit (typ: Type) =
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

        // save element index; load structs by reference to conserve stack space
        let etype = typ.GetElementType()
        let cmd = if isStruct etype then OpCodes.Ldelema else OpCodes.Ldelem

        emitSaveValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0); gen.Emit(cmd, etype)) etype

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

    let emitSave (gen: ILGenerator) (typ: Type) =
        // serialize object contents
        if typ.IsValueType then
            emitSaveValue gen (fun gen -> gen.Emit(OpCodes.Ldarg_2); gen.Emit(OpCodes.Unbox_Any, typ)) typ
        else
            let save =
                if typ = typedefof<string> || typ = typedefof<byte array> || typ = typedefof<char array> then
                    emitSaveValuePrimitive
                else if typ.IsArray then
                    assert (typ.GetArrayRank() = 1)
                    emitSaveArray
                else
                    emitSaveFields
                    
            save gen (fun gen -> gen.Emit(OpCodes.Ldarg_2)) typ

        gen.Emit(OpCodes.Ret)

    let buildSaveDelegate (typ: Type) =
        let dm = DynamicMethod(typ.ToString(), null, [|typedefof<ObjectTable>; typedefof<BinaryWriter>; typedefof<obj>|], typedefof<SaveMethodHost>, true)
        let gen = dm.GetILGenerator()

        emitSave gen typ

        dm.CreateDelegate(typedefof<SaveDelegate>) :?> SaveDelegate

    let saveDelegateCache = Dictionary<Type, SaveDelegate>()

    let getSaveDelegate typ =
        match saveDelegateCache.TryGetValue(typ) with
        | true, value -> value
        | _ ->
            let d = buildSaveDelegate typ
            saveDelegateCache.Add(typ, d)
            d

    let save (context: ObjectTable) obj =
        use stream = new MemoryStream()
        use writer = new BinaryWriter(stream)

        // push head object
        context.Object obj |> ignore

        // save all objects (additional items are pushed to the queue in save delegates)
        for obj in context.PendingObjects do
            let d = getSaveDelegate (obj.GetType())
            d.Invoke(context, writer, obj)

        stream.ToArray()

let saveToStream stream obj =
    let table = Saver.ObjectTable()

    // save object data & fill object table
    let data = Saver.save table obj

    // build type table and object -> type id mapping
    let types = Dictionary<Type, int>()

    let object_types = table.Objects |> Array.map (fun p ->
        let typ = p.Key.GetType()

        match types.TryGetValue(typ) with
        | true, id -> id
        | _ ->
            let id = types.Count
            types.Add(typ, id)
            id)

    // save header
    let writer = new BinaryWriter(stream)

    writer.Write("fun")

    // save type table
    writer.Write(types.Count)

    types |> Array.ofSeq |> Array.sortBy (fun p -> p.Value) |> Array.map (fun p -> p.Key.AssemblyQualifiedName) |> Array.iter writer.Write

    // save object table
    writer.Write(object_types.Length)

    object_types |> Array.iter writer.Write

    // save array size table
    table.Objects |> Array.choose (fun p ->
        let typ = p.Key.GetType()
        if typ.IsArray then
            Some (p.Key :?> System.Array).Length
        else
            None) |> Array.iter writer.Write

    // save object data
    writer.Write(data)

let saveToFile path obj =
    use stream = new FileStream(path, FileMode.Create)
    saveToStream stream obj