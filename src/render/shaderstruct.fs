namespace Render

open System
open System.Reflection
open System.Reflection.Emit
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

    // get offsets for all reflected properties and the total structure size
    let rec private getPropertyOffsets (typ: Type) =
        // round a value up to 16 bytes
        let round16 v = (v + 15) / 16 * 16

        // get all reflected properties & their sizes
        let props = getProperties typ
        let sizes = props |> Array.map (fun p -> let pt = p.PropertyType in if pt.IsClass then snd (getPropertyOffsets pt) else primitive_types.[pt])

        // for each property, get the property *end* offset by applying HLSL packing rules
        let offsets =
            sizes |> Array.scan (fun off size ->
                // round starting offset to 16 bytes if new value straddles the 16b boundary
                if size < 16 && ((off + size) % 16 = 0 || (off + size) % 16 > off % 16) then
                    off + size
                else
                    round16 off + size) 0

        // convert property end offsets to property start offsets & round size up
        Array.map2 (-) (Array.sub offsets 1 (offsets.Length - 1)) sizes, round16 offsets.[offsets.Length - 1]

    // a delegate for uploading objects to memory
    type UploadDelegate = delegate of obj * nativeint * int -> unit

    // all upload methods are created as methods of this type
    type private UploadMethodHost = class end

    // upload object to memory
    let private emitUploadObject (gen: ILGenerator) (p: PropertyInfo) (offset: int) =
        // load object
        gen.Emit(OpCodes.Ldloc_0)
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
        gen.Emit(OpCodes.Call, typedefof<UploadMethodHost>.GetMethod("upload " + p.PropertyType.Name))

    // upload value to memory
    let private emitUploadValue (gen: ILGenerator) (p: PropertyInfo) (offset: int) =
        // compute address
        gen.Emit(OpCodes.Ldarg_1)
        gen.Emit(OpCodes.Ldc_I4, offset)
        gen.Emit(OpCodes.Add)

        // load value
        gen.Emit(OpCodes.Ldloc_0)
        gen.Emit(OpCodes.Call, p.GetGetMethod())

        // store object
        gen.Emit(OpCodes.Stobj, p.PropertyType)

    // upload struct to memory
    let private emitUpload (gen: ILGenerator) (typ: Type) =
        // get type layout
        let props = getProperties typ
        let offsets, size = getPropertyOffsets typ

        // check buffer size
        gen.Emit(OpCodes.Ldarg_2)
        gen.Emit(OpCodes.Ldc_I4, size)
        gen.Emit(OpCodes.Sub_Ovf_Un)
        gen.Emit(OpCodes.Pop)

        // cast to proper type
        let obj = gen.DeclareLocal(typ)
        assert (obj.LocalIndex = 0)

        gen.Emit(OpCodes.Ldarg_0)
        gen.Emit(OpCodes.Castclass, typ)
        gen.Emit(OpCodes.Stloc_0)

        // write everything
        props |> Array.iteri (fun i p -> (if p.PropertyType.IsClass then emitUploadObject else emitUploadValue) gen p offsets.[i])

        // all done
        gen.Emit(OpCodes.Ret)

        size

    // create an upload delegate for a given type
    let private buildUploadDelegate (typ: Type) =
        let dm = DynamicMethod("upload " + typ.ToString(), null, [|typedefof<obj>; typedefof<nativeint>; typedefof<int>|], typedefof<UploadMethodHost>, skipVisibility = true)
        let gen = dm.GetILGenerator()

        let size = emitUpload gen typ

        size, dm.CreateDelegate(typedefof<UploadDelegate>) :?> UploadDelegate

    // a cache for upload delegates (one delegate per type)
    let private uploadDelegateCache = Core.ConcurrentCache<Type, _>(buildUploadDelegate)

    // public accessor for upload delegate cache
    let getUploadDelegate typ = uploadDelegateCache.Get typ