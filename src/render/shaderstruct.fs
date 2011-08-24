namespace Render

open System
open System.Reflection
open System.Runtime.InteropServices

// attribute for types that should be mapped to shader structs
type ShaderStructAttribute() = class end

module ShaderStruct =
    // primitive types
    let private primitive_types =
        [|typedefof<float32>; typedefof<int>; typedefof<bool>; typedefof<Vector2>; typedefof<Vector3>; typedefof<Vector4>; typedefof<Matrix34>; typedefof<Matrix44>|]
        |> Array.map (fun t -> t, Marshal.SizeOf(t))
        |> dict
    
    // get all properties than should be reflected to shader
    let getProperties (typ: Type) =
        typ.GetProperties(BindingFlags.Instance ||| BindingFlags.Public)
        |> Array.filter (fun p ->
            let pt = p.PropertyType
            p.CanRead && (primitive_types.ContainsKey(pt) || pt.IsDefined(typedefof<ShaderStructAttribute>, false)))