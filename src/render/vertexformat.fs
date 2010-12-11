namespace Render

open SlimDX
open SlimDX.DXGI
open SlimDX.Direct3D11

type VertexFormat =
    { elements: InputElement array
      size: int }

module VertexFormatBuilder =
    // get a size of the format element
    let private getFormatSize element =
        match element with
        | Format.R32G32B32A32_Typeless | Format.R32G32B32A32_Float | Format.R32G32B32A32_UInt | Format.R32G32B32A32_SInt -> 16
        | Format.R32G32B32_Typeless | Format.R32G32B32_Float | Format.R32G32B32_UInt | Format.R32G32B32_SInt -> 12
        | Format.R16G16B16A16_Typeless | Format.R16G16B16A16_Float | Format.R16G16B16A16_UNorm | Format.R16G16B16A16_UInt | Format.R16G16B16A16_SNorm | Format.R16G16B16A16_SInt -> 8
        | Format.R32G32_Typeless | Format.R32G32_Float | Format.R32G32_UInt | Format.R32G32_SInt -> 8
        | Format.R32G8X24_Typeless | Format.D32_Float_S8X24_UInt | Format.R32_Float_X8X24_Typeless | Format.X32_Typeless_G8X24_UInt -> 4
        | Format.R10G10B10A2_Typeless | Format.R10G10B10A2_UNorm | Format.R10G10B10A2_UInt -> 4
        | Format.R11G11B10_Float -> 4
        | Format.R8G8B8A8_Typeless | Format.R8G8B8A8_UNorm | Format.R8G8B8A8_UNorm_SRGB | Format.R8G8B8A8_UInt | Format.R8G8B8A8_SNorm | Format.R8G8B8A8_SInt -> 4
        | Format.R16G16_Typeless | Format.R16G16_Float | Format.R16G16_UNorm | Format.R16G16_UInt | Format.R16G16_SNorm | Format.R16G16_SInt -> 4
        | Format.R32_Typeless | Format.D32_Float | Format.R32_Float | Format.R32_UInt | Format.R32_SInt -> 4
        | Format.R24G8_Typeless | Format.D24_UNorm_S8_UInt | Format.R24_UNorm_X8_Typeless | Format.X24_Typeless_G8_UInt -> 4
        | Format.R8G8_Typeless | Format.R8G8_UNorm | Format.R8G8_UInt | Format.R8G8_SNorm | Format.R8G8_SInt -> 2
        | Format.R16_Typeless | Format.R16_Float | Format.D16_UNorm | Format.R16_UNorm | Format.R16_UInt | Format.R16_SNorm | Format.R16_SInt -> 2
        | Format.R8_Typeless | Format.R8_UNorm | Format.R8_UInt | Format.R8_SNorm | Format.R8_SInt | Format.A8_UNorm -> 1
        | Format.R9G9B9E5_SharedExp -> 4
        | _ -> failwith "Unknown format element"

    // build a format from an array of (semantics, index, format) tuples
    let build components =
        // get component offsets
        let offsets = components |> Array.scan (fun acc (semantics, index, format) -> acc + getFormatSize format) 0

        // get input elements
        let elements = components |> Array.mapi (fun i (semantics, index, format) -> InputElement(semantics, index, format, offsets.[i], 0))

        // build vertex format
        { new VertexFormat with elements = elements and size = offsets.[offsets.Length - 1] }

module VertexFormats =
    let Pos_TBN_Tex1_Bone4_Packed =
        VertexFormatBuilder.build
            [|
                "POSITION", 0, Format.R16G16B16A16_UNorm;
                "NORMAL", 0, Format.R8G8B8A8_SNorm;
                "TANGENT", 0, Format.R8G8B8A8_SNorm;
                "TEXCOORD", 0, Format.R16G16_UNorm;
                "BONEINDICES", 0, Format.R8G8B8A8_UInt;
                "BONEWEIGHTS", 0, Format.R8G8B8A8_UNorm;
            |]