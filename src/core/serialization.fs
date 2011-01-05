module Core.Serialization

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Reflection.Emit

type ObjectSaveContext() =
    let object_queue = Queue<obj>()
    let object_ids = Dictionary<obj, int>(HashIdentity.Reference)

    // get the list of objects to be serialized
    member x.Objects = seq { while object_queue.Count > 0 do yield object_queue.Dequeue() }

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

type ObjectSaveDelegate = delegate of ObjectSaveContext * BinaryWriter * obj -> unit

let loadFromStream stream =
    null

let loadFromFile path =
    use stream = new FileStream(path, FileMode.Open)
    loadFromStream stream

module Saver =
    type SaveMethodHost = class end

    let isStruct (typ: Type) =
        typ.IsValueType && not typ.IsPrimitive && not typ.IsEnum
        
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
            gen.Emit(OpCodes.Ldarg_0) // context
            objemit gen
            gen.Emit(OpCodes.Call, typedefof<ObjectSaveContext>.GetMethod("Object"))

            // write object id
            gen.Emit(OpCodes.Call, typedefof<BinaryWriter>.GetMethod("Write", [|typedefof<int>|]))

    and emitSaveFields (gen: ILGenerator) objemit (typ: Type) =
        if not typ.IsSerializable then failwith (sprintf "Type %A is not serializable" typ)

        // get all fields
        let fields = typ.FindMembers(MemberTypes.Field, BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public, null, null) |> Array.map (fun f -> f :?> FieldInfo)

        // get all serializable fields
        let fields_ser = fields |> Array.filter (fun f -> not f.IsNotSerialized)

        // can't have empty types
        assert (fields_ser.Length > 0)

        // serialize all serializable fields
        for f in fields_ser do
            // load structs by reference to conserve stack space
            let ldfld = if isStruct f.FieldType then OpCodes.Ldflda else OpCodes.Ldfld

            emitSaveValue gen (fun gen -> objemit gen; gen.Emit(ldfld, f)) f.FieldType

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

        // serialize size
        gen.Emit(OpCodes.Ldarg_1) // writer
        gen.Emit(OpCodes.Ldloc_1) // count
        gen.Emit(OpCodes.Call, typedefof<BinaryWriter>.GetMethod("Write", [|typedefof<int>|]))

        // serialize contents
        let loop_begin = gen.DefineLabel()
        let loop_cmp = gen.DefineLabel()

        gen.Emit(OpCodes.Br, loop_cmp)
        gen.MarkLabel(loop_begin)

        // save element index; load structs by reference to conserve stack space
        let etype = typ.GetElementType()
        let ldelem = if isStruct etype then OpCodes.Ldelema else OpCodes.Ldelem

        emitSaveValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0); gen.Emit(ldelem, etype)) etype

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
                if typ.IsArray then
                    assert (typ.GetArrayRank() = 1)
                    emitSaveArray
                else if typ = typedefof<string> || typ = typedefof<byte array> || typ = typedefof<char array> then
                    emitSaveValuePrimitive
                else
                    emitSaveFields
                    
            save gen (fun gen -> gen.Emit(OpCodes.Ldarg_2)) typ

        gen.Emit(OpCodes.Ret)

    let buildSaveDelegate (typ: Type) =
        let dm = DynamicMethod(typ.ToString(), null, [|typedefof<ObjectSaveContext>; typedefof<BinaryWriter>; typedefof<obj>|], typedefof<SaveMethodHost>, true)
        let gen = dm.GetILGenerator()

        emitSave gen typ

        dm.CreateDelegate(typedefof<ObjectSaveDelegate>) :?> ObjectSaveDelegate

    let saveDelegateCache = Dictionary<Type, ObjectSaveDelegate>()

    let getSaveDelegate typ =
        match saveDelegateCache.TryGetValue(typ) with
        | true, value -> value
        | _ ->
            let d = buildSaveDelegate typ
            saveDelegateCache.Add(typ, d)
            d

    let saveWithContext context obj =
        ()

let saveToStream stream obj =
    let writer = new BinaryWriter(stream)
    let context = ObjectSaveContext()

    // push head object
    context.Object obj |> ignore

    // save all objects (additional items are pushed to the queue in save delegates)
    for obj in context.Objects do
        let d = Saver.getSaveDelegate (obj.GetType())
        d.Invoke(context, writer, obj)

let saveToFile path obj =
    use stream = new FileStream(path, FileMode.Create)
    saveToStream stream obj