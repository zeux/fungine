module Core.Serialization

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Reflection.Emit

type ObjectSaveContext(writer: BinaryWriter) =
    let object_queue = Queue<obj>()
    let object_ids = Dictionary<obj, int>(HashIdentity.Reference)

    // get associated writer
    member x.Writer = writer

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

type ObjectSaveDelegate = delegate of ObjectSaveContext * obj -> unit

let loadFromStream stream =
    null

let loadFromFile path =
    use stream = new FileStream(path, FileMode.Open)
    loadFromStream stream

module Saver =
    type SaveMethodHost = class end

    let emitSaveFieldPrimitive (gen: ILGenerator) objemit (field: FieldInfo) save_type =
        gen.Emit(OpCodes.Ldloc_0) // writer
        objemit gen // object
        gen.Emit(OpCodes.Ldfld, field)
        gen.Emit(OpCodes.Call, typedefof<BinaryWriter>.GetMethod("Write", [|save_type|]))

    let rec emitSaveField (gen: ILGenerator) objemit (field: FieldInfo) =
        let typ = field.FieldType

        // save primitive types as is
        if typ.IsPrimitive then
            emitSaveFieldPrimitive gen objemit field field.FieldType
        // save enums as int values
        else if typ.IsEnum then
            emitSaveFieldPrimitive gen objemit field typedefof<int>
        // save structs as embedded field lists
        else if typ.IsValueType then
            emitSaveFields gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldflda, field)) field.FieldType
        // save objects as object ids (defer actual saving)
        else
            assert typ.IsClass
            gen.Emit(OpCodes.Ldloc_0) // writer

            // push object id
            gen.Emit(OpCodes.Ldarg_0) // context
            objemit gen // object
            gen.Emit(OpCodes.Ldfld, field) // field
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

        // serialize all serializable fields (!)
        for f in fields_ser do
            emitSaveField gen objemit f

    let emitSaveArray (gen: ILGenerator) (typ: Type) =
        // store size
        gen.Emit(OpCodes.Ldloc_0)
        gen.Emit(OpCodes.Ldloc_1)
        gen.Emit(OpCodes.Ldlen)
        gen.Emit(OpCodes.Call, typedefof<BinaryWriter>.GetMethod("Write", [|typedefof<int>|]))

        let element_type = typ.GetElementType()

        if element_type.IsValueType then
            // store value type arrays
            ()
        else
            ()

    let emitSave (gen: ILGenerator) (typ: Type) =
        // store writer to the local variable
        let writer_local = gen.DeclareLocal(typedefof<BinaryWriter>)
        assert (writer_local.LocalIndex = 0)

        gen.Emit(OpCodes.Ldarg_0) // context
        gen.EmitCall(OpCodes.Call, typedefof<ObjectSaveContext>.GetProperty("Writer").GetGetMethod(), null)
        gen.Emit(OpCodes.Stloc_0) // writer

        // store object with the correct type to the local variable
        let object_local = gen.DeclareLocal(typ)
        assert (object_local.LocalIndex = 1)
        assert typ.IsClass

        gen.Emit(OpCodes.Ldarg_1)
        gen.Emit(OpCodes.Castclass, typ)
        gen.Emit(OpCodes.Stloc_1)

        if typ.IsArray then
            assert (typ.GetArrayRank() = 1)
            emitSaveArray gen typ
        else
            emitSaveFields gen (fun gen -> gen.Emit(OpCodes.Ldloc_1)) typ

        gen.Emit(OpCodes.Ret)

    let buildSaveDelegate (typ: Type) =
        let dm = DynamicMethod("", null, [|typedefof<ObjectSaveContext>; typedefof<obj>|], typedefof<SaveMethodHost>, true)
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
    let context = ObjectSaveContext(writer)

    // push head object
    context.Object obj |> ignore

    // save all objects (additional items are pushed to the queue in save delegates)
    for obj in context.Objects do
        let d = Saver.getSaveDelegate (obj.GetType())
        d.Invoke(context, obj)

let saveToFile path obj =
    use stream = new FileStream(path, FileMode.Create)
    saveToStream stream obj