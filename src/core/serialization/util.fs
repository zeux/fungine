module Core.Serialization.Util

open System
open System.Collections.Generic
open System.Reflection
open System.Reflection.Emit

// return true if type is a struct
let isStruct (typ: Type) =
    typ.IsValueType && not typ.IsPrimitive && not typ.IsEnum

// get all fields from the type and its ancestors that are eligible for serialization
let getSerializableFields (typ: Type) =
    if not typ.IsSerializable then failwithf "Type %A is not serializable" typ

    // get all fields
    let fields = typ.FindMembers(MemberTypes.Field, BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public, null, null) |> Array.map (fun f -> f :?> FieldInfo)

    // get all serializable fields
    let fieldsSer = fields |> Array.filter (fun f -> not f.IsNotSerialized)

    fieldsSer

// emit a loop that iterates through all array elements (leaving array index in loc0)
let emitArrayLoop (gen: ILGenerator) objemit bodyemit =
    // declare local variables for length and for loop counter
    let idxLocal = gen.DeclareLocal(typeof<int>)
    assert (idxLocal.LocalIndex = 0)

    let cntLocal = gen.DeclareLocal(typeof<int>)
    assert (cntLocal.LocalIndex = 1)

    // store size to local
    objemit gen
    gen.Emit(OpCodes.Ldlen)
    gen.Emit(OpCodes.Stloc_1) // count

    // jump to the loop comparison part (needed for empty arrays)
    let loopCmp = gen.DefineLabel()
    gen.Emit(OpCodes.Br, loopCmp)

    // loop body
    let loopBegin = gen.DefineLabel()
    gen.MarkLabel(loopBegin)

    bodyemit gen

    // index++
    gen.Emit(OpCodes.Ldloc_0) // index
    gen.Emit(OpCodes.Ldc_I4_1)
    gen.Emit(OpCodes.Add)
    gen.Emit(OpCodes.Stloc_0)

    // if (index < count) goto begin
    gen.MarkLabel(loopCmp)
    gen.Emit(OpCodes.Ldloc_0) // index
    gen.Emit(OpCodes.Ldloc_1) // count
    gen.Emit(OpCodes.Blt, loopBegin)

// encoding for serialized strings
let stringEncoding = System.Text.UTF8Encoding()
