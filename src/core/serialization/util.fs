module Core.Serialization.Util

open System
open System.Collections.Generic
open System.Reflection
open System.Reflection.Emit

// a cache based on types
type TypeCache<'a>(creator) =
    let cache = Dictionary<Type, 'a>()

    // get the value from the cache, creating it as necessary
    member this.Get typ =
        match cache.TryGetValue(typ) with
        | true, value -> value
        | _ ->
            let d = creator typ
            cache.Add(typ, d)
            d

// return true if type is a struct
let isStruct (typ: Type) =
    typ.IsValueType && not typ.IsPrimitive && not typ.IsEnum

// get all fields from the type and its ancestors that are eligible for serialization
let getSerializableFields (typ: Type) =
    if not typ.IsSerializable then failwithf "Type %A is not serializable" typ

    // get all fields
    let fields = typ.FindMembers(MemberTypes.Field, BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public, null, null) |> Array.map (fun f -> f :?> FieldInfo)

    // get all serializable fields
    let fields_ser = fields |> Array.filter (fun f -> not f.IsNotSerialized)

    fields_ser

// emit a loop that iterates through all array elements (leaving array index in loc0)
let emitArrayLoop (gen: ILGenerator) objemit bodyemit =
    // declare local variables for length and for loop counter
    let idx_local = gen.DeclareLocal(typedefof<int>)
    assert (idx_local.LocalIndex = 0)

    let cnt_local = gen.DeclareLocal(typedefof<int>)
    assert (cnt_local.LocalIndex = 1)

    // store size to local
    objemit gen
    gen.Emit(OpCodes.Ldlen)
    gen.Emit(OpCodes.Stloc_1) // count

    // jump to the loop comparison part (needed for empty arrays)
    let loop_cmp = gen.DefineLabel()
    gen.Emit(OpCodes.Br, loop_cmp)

    // loop body
    let loop_begin = gen.DefineLabel()
    gen.MarkLabel(loop_begin)

    bodyemit gen

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

// encoding for serialized strings
let stringEncoding = System.Text.UTF8Encoding()