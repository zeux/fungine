module Core.Serialization.Util

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
