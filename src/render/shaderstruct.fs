namespace Render

open System
open System.Reflection
open System.Reflection.Emit
open System.Runtime.InteropServices

// attribute for types that should be mapped to shader structs
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct)>]
type ShaderStructAttribute() =
    inherit Attribute()

// attribute for properties that should be mapped to shader arrays
[<AttributeUsage(AttributeTargets.Property)>]
type ShaderArrayAttribute(length) =
    inherit Attribute()
    member this.Length = length

module ShaderStruct =
    // primitive types
    let private primitiveTypes =
        [|typeof<float32>; typeof<int>; typeof<bool>; typeof<Vector2>; typeof<Vector3>; typeof<Vector4>; typeof<Matrix34>; typeof<Matrix44>; typeof<Color4>|]
        |> Array.map (fun t -> t, Marshal.SizeOf(t))
        |> dict

    // get all properties than should be reflected to shader
    let getProperties (typ: Type) =
        typ.GetProperties(BindingFlags.Instance ||| BindingFlags.Public)
        |> Array.choose (fun p ->
            let pt = if p.GetCustomAttribute(typeof<ShaderArrayAttribute>) <> null then p.PropertyType.GetElementType() else p.PropertyType
            if p.CanRead && (primitiveTypes.ContainsKey(pt) || pt.IsDefined(typeof<ShaderStructAttribute>, false)) then
                Some (p, pt)
            else
                None)

    // round a value up to 16 bytes
    let private round16 v = (v + 15) &&& ~~~15

    // get offsets for all reflected properties and the total structure size
    let rec private getPropertyOffsets (typ: Type) =
        // get all reflected properties & their sizes
        let props = getProperties typ
        let sizes =
            props |> Array.map (fun (p, pt) ->
                let elementSize =
                    if pt.IsClass then snd (getPropertyOffsets pt)
                    elif pt.IsEnum then primitiveTypes.[typeof<int>]
                    else primitiveTypes.[pt]

                match p.GetCustomAttribute(typeof<ShaderArrayAttribute>) with
                | :? ShaderArrayAttribute as attr -> attr.Length * round16 elementSize
                | _ -> elementSize)

        // for each property, get the property *end* offset by applying HLSL packing rules
        let offsets =
            sizes |> Array.scan (fun off size ->
                // round starting offset to 16 bytes if new value straddles the 16b boundary
                if off / 16 = (off + size - 1) / 16 then
                    off + size
                else
                    round16 off + size) 0

        // convert property end offsets to property start offsets & round size up
        Array.map2 (-) (Array.sub offsets 1 (offsets.Length - 1)) sizes, round16 offsets.[offsets.Length - 1]

    // a delegate for uploading objects to memory
    type UploadDelegate = delegate of obj * nativeint * int -> unit

    // all upload methods are created as methods of this type
    type private UploadMethodHost = class end

    // upload property with reference type to memory
    let private emitUploadPropertyReference (gen: ILGenerator) objemit (p: PropertyInfo) (offset: int) =
        // load object
        objemit gen
        gen.Emit(OpCodes.Call, p.GetGetMethod())

        // compute address
        gen.Emit(OpCodes.Ldarg_1)
        gen.Emit(OpCodes.Ldc_I4, offset)
        gen.Emit(OpCodes.Add)

        // compute remaining buffer space
        gen.Emit(OpCodes.Ldarg_2)
        gen.Emit(OpCodes.Ldc_I4, offset)
        gen.Emit(OpCodes.Sub)

        // call upload method
        gen.Emit(OpCodes.Call, Type.GetType("Render.ShaderStruct").GetMethod("uploadStub", BindingFlags.NonPublic ||| BindingFlags.Static))

    // convert value to shader-ready type
    let private emitConvertValue (gen: ILGenerator) typ =
        if typ = typeof<bool> then
            typeof<int>
        else
            typ

    // upload value type to memory
    let private emitUploadValue (gen: ILGenerator) objemit (typ: Type) (offset: int) =
        // compute address
        gen.Emit(OpCodes.Ldarg_1)
        gen.Emit(OpCodes.Ldc_I4, offset)
        gen.Emit(OpCodes.Add)

        // load value
        objemit gen

        // store object
        let styp = emitConvertValue gen typ
        gen.Emit(OpCodes.Stobj, styp)

    // upload property with value type to memory
    let private emitUploadPropertyValue (gen: ILGenerator) objemit (p: PropertyInfo) (offset: int) =
        emitUploadValue gen (fun gen ->
            objemit gen
            gen.Emit(OpCodes.Call, p.GetGetMethod())) p.PropertyType offset

    // emit a loop that iterates through all array elements and updates buffer using the element size
    let private emitArrayLoopUpdateBuffer (gen: ILGenerator) (size: int) bodyemit =
        Core.Serialization.Util.emitArrayLoop gen
            (fun gen -> gen.Emit(OpCodes.Ldloc_0))
            (fun gen idx ->
                bodyemit gen idx
    
                // buffer address += element size
                gen.Emit(OpCodes.Ldarg_1)
                gen.Emit(OpCodes.Ldc_I4, size)
                gen.Emit(OpCodes.Add)
                gen.Emit(OpCodes.Starg_S, 1uy)
    
                // buffer size -= element size
                gen.Emit(OpCodes.Ldarg_2)
                gen.Emit(OpCodes.Ldc_I4, size)
                gen.Emit(OpCodes.Sub)
                gen.Emit(OpCodes.Starg_S, 2uy))

    // upload struct to memory
    let private emitUpload (gen: ILGenerator) (typ: Type) (vtyp: Type) =
        // get type layout
        let props, offsets, size =
            if primitiveTypes.ContainsKey(vtyp) then
                None, [|0|], round16 primitiveTypes.[vtyp]
            elif vtyp.IsEnum then
                None, [|0|], round16 primitiveTypes.[typeof<int>]
            else
                let offsets, size = getPropertyOffsets vtyp
                Some (getProperties vtyp), offsets, size

        // check buffer size
        gen.Emit(OpCodes.Ldarg_2)
        gen.Emit(OpCodes.Ldc_I4, size)

        // multiply element size by element count in case of array
        if typ.IsArray then
            gen.Emit(OpCodes.Ldarg_0)
            gen.Emit(OpCodes.Ldlen)
            gen.Emit(OpCodes.Mul)

        gen.Emit(OpCodes.Sub_Ovf_Un)
        gen.Emit(OpCodes.Pop)

        // cast to proper type
        let objLocal = gen.DeclareLocal(typ)

        gen.Emit(OpCodes.Ldarg_0)
        gen.Emit(OpCodes.Unbox_Any, typ)
        gen.Emit(OpCodes.Stloc, objLocal)

        // write a single element (object has one element, array has several elements)
        let emitElement gen objemit =
            match props with
            | Some pl -> pl |> Array.iteri (fun i (p, pt) -> (if p.PropertyType.IsClass then emitUploadPropertyReference else emitUploadPropertyValue) gen objemit p offsets.[i])
            | None -> emitUploadValue gen objemit vtyp offsets.[0]

        // write the entire object
        if typ.IsArray then
            emitArrayLoopUpdateBuffer gen size (fun gen idx ->
                emitElement gen (fun gen ->
                    gen.Emit(OpCodes.Ldloc, objLocal)
                    gen.Emit(OpCodes.Ldloc, idx)
                    gen.Emit(OpCodes.Ldelem, vtyp)))
        else
            emitElement gen (fun gen -> gen.Emit(OpCodes.Ldloc, objLocal))

        // all done
        gen.Emit(OpCodes.Ret)

        size

    // create an upload delegate for a given type
    let private buildUploadDelegate (typ: Type) =
        let vtyp = if typ.IsArray then typ.GetElementType() else typ
        if not (primitiveTypes.ContainsKey(vtyp)) && not (vtyp.IsDefined(typeof<ShaderStructAttribute>, false)) then failwithf "Type %A does not have ShaderStruct attribute" typ

        let dm = DynamicMethod("upload " + typ.ToString(), null, [|typeof<obj>; typeof<nativeint>; typeof<int>|], typeof<UploadMethodHost>, skipVisibility = true)
        let gen = dm.GetILGenerator()

        let size = emitUpload gen typ vtyp

        size, dm.CreateDelegate(typeof<UploadDelegate>) :?> UploadDelegate

    // a cache for upload delegates (one delegate per type)
    let private uploadDelegateCache = Core.ConcurrentCache<Type, _>(buildUploadDelegate)

    // public accessor for upload delegate cache
    let getUploadDelegate typ = uploadDelegateCache.Get typ

    // private upload stub for reference roundtripping (not very optimal, but will do for now)
    let private uploadStub (obj: obj) (addr: nativeint) (size: int) =
        let _, upload = getUploadDelegate $ obj.GetType()
        upload.Invoke(obj, addr, size)