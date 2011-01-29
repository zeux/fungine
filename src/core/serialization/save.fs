module Core.Serialization.Save

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Reflection.Emit

// a table that holds all objects in a current graph
type private ObjectTable() =
    let object_queue = Queue<obj>()
    let object_ids = Dictionary<obj, int>(HashIdentity.Reference)

    // get the list of objects to be serialized
    member this.PendingObjects = seq { while object_queue.Count > 0 do yield object_queue.Dequeue() }

    // get the list of all objects
    member this.Objects = object_ids |> Array.ofSeq

    // get a serialized id of the object
    member this.Object (obj: obj) =
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

// a delegate for saving objects; there is an instance of one for each type
type private SaveDelegate = delegate of ObjectTable * BinaryWriter * obj -> unit

// all save methods are created as methods of this type
type private SaveMethodHost = class end

// save any value for which there is a BinaryWriter.Write method (all primitive types, string, byte array)
let private emitSaveValuePrimitive (gen: ILGenerator) objemit typ =
    gen.Emit(OpCodes.Ldarg_1) // writer
    objemit gen

    // special handling for chars since BinaryWriter saves them as UTF-8
    gen.Emit(OpCodes.Call, typedefof<BinaryWriter>.GetMethod("Write", [| (if typ = typedefof<char> then typedefof<int16> else typ) |]))

// save an object
let private emitSaveObject (gen: ILGenerator) objemit =
    gen.Emit(OpCodes.Ldarg_1) // writer

    // push object id
    gen.Emit(OpCodes.Ldarg_0) // table
    objemit gen
    gen.Emit(OpCodes.Call, typedefof<ObjectTable>.GetMethod("Object", BindingFlags.Instance ||| BindingFlags.NonPublic))

    // write object id
    gen.Emit(OpCodes.Call, typedefof<BinaryWriter>.GetMethod("Write", [|typedefof<int>|]))

// save any value (dispatcher function)
let rec private emitSaveValue (gen: ILGenerator) objemit (typ: Type) =
    // save primitive types as is
    if typ.IsPrimitive then
        emitSaveValuePrimitive gen objemit typ
    // save enums as integer values
    else if typ.IsEnum then
        emitSaveValuePrimitive gen objemit (typ.GetEnumUnderlyingType())
    // save structs as embedded field lists
    else if typ.IsValueType then
        emitSaveFields gen objemit typ
    // save objects as object ids (defer actual saving)
    else
        assert typ.IsClass
        emitSaveObject gen objemit

// save all fields of a class/struct
and private emitSaveFields (gen: ILGenerator) objemit (typ: Type) =
    let fields = Util.getSerializableFields typ

    // serialize all serializable fields
    for f in fields do
        // load structs by reference to conserve stack space
        let cmd = if Util.isStruct f.FieldType then OpCodes.Ldflda else OpCodes.Ldfld

        emitSaveValue gen (fun gen -> objemit gen; gen.Emit(cmd, f)) f.FieldType

// save an array
let private emitSaveArray (gen: ILGenerator) objemit (typ: Type) =
    // save element index; load structs by reference to conserve stack space
    let etype = typ.GetElementType()
    let cmd = if Util.isStruct etype then OpCodes.Ldelema else OpCodes.Ldelem

    Util.emitArrayLoop gen objemit (fun gen ->
        emitSaveValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Ldloc_0); gen.Emit(cmd, etype)) etype)

// save a top-level type
let private emitSave (gen: ILGenerator) (typ: Type) =
    // serialize object contents
    let objemit (gen: ILGenerator) = gen.Emit(OpCodes.Ldarg_2)

    if typ.IsValueType then
        emitSaveValue gen (fun gen -> objemit gen; gen.Emit(OpCodes.Unbox_Any, typ)) typ
    else if typ = typedefof<string> || typ = typedefof<byte array> then
        emitSaveValuePrimitive gen objemit typ
    else if typ.IsArray then
        assert (typ.GetArrayRank() = 1)
        emitSaveArray gen objemit typ
    else
        emitSaveFields gen objemit typ

    gen.Emit(OpCodes.Ret)

// create a save delegate for a given type
let private buildSaveDelegate (typ: Type) =
    let dm = DynamicMethod(typ.ToString(), null, [|typedefof<ObjectTable>; typedefof<BinaryWriter>; typedefof<obj>|], typedefof<SaveMethodHost>, true)
    let gen = dm.GetILGenerator()

    emitSave gen typ

    dm.CreateDelegate(typedefof<SaveDelegate>) :?> SaveDelegate

// a cache for save delegates (one delegate per type)
let private saveDelegateCache = Util.TypeCache(buildSaveDelegate)

// save object data to a data array
let private save (context: ObjectTable) obj =
    use stream = new MemoryStream()
    use writer = new BinaryWriter(stream)

    // push head object
    context.Object obj |> ignore

    // save all objects (additional items are pushed to the queue in save delegates)
    for obj in context.PendingObjects do
        let d = saveDelegateCache.Get(obj.GetType())

        d.Invoke(context, writer, obj)

    stream.ToArray()

// save object to stream
let toStream stream obj =
    let table = ObjectTable()

    // save object data & fill object table
    let data = save table obj

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
    let writer = new BinaryWriter(stream, Util.stringEncoding)

    writer.Write("fun")

    // save type table
    writer.Write(types.Count)

    let type_table = types |> Array.ofSeq |> Array.sortBy (fun p -> p.Value) |> Array.map (fun p -> p.Key)

    type_table |> Array.iter (fun typ -> writer.Write(typ.AssemblyQualifiedName))
    type_table |> Array.iter (fun typ -> writer.Write(Version.get typ))

    // save object table
    writer.Write(object_types.Length)

    object_types |> Array.iter writer.Write

    // save array size table
    table.Objects |> Array.choose (fun p ->
        let typ = p.Key.GetType()
        if typ.IsArray then
            Some (p.Key :?> System.Array).Length
        else if typ = typedefof<string> then
            Some (p.Key :?> string).Length
        else
            None) |> Array.iter writer.Write

    // save object data
    writer.Write(data)

// save object to file
let toFile path obj =
    use stream = new FileStream(path, FileMode.Create)
    toStream stream obj