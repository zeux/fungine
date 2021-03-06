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
    // declare local variable for loop counter
    let idxLocal = gen.DeclareLocal(typeof<int>)

    // jump to the loop comparison part (needed for empty arrays)
    let loopCmp = gen.DefineLabel()
    gen.Emit(OpCodes.Br, loopCmp)

    // loop body
    let loopBegin = gen.DefineLabel()
    gen.MarkLabel(loopBegin)

    bodyemit gen idxLocal

    // index++
    gen.Emit(OpCodes.Ldloc, idxLocal)
    gen.Emit(OpCodes.Ldc_I4_1)
    gen.Emit(OpCodes.Add)
    gen.Emit(OpCodes.Stloc, idxLocal)

    // if (index < count) goto begin
    gen.MarkLabel(loopCmp)
    gen.Emit(OpCodes.Ldloc, idxLocal)
    objemit gen
    gen.Emit(OpCodes.Ldlen) // count
    gen.Emit(OpCodes.Blt, loopBegin)

// encoding for serialized strings
let stringEncoding = System.Text.UTF8Encoding()
