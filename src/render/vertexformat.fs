namespace Render

open SharpDX.Direct3D11

// layout of the vertex data
type VertexLayout =
    { elements: InputElement array
      size: int }

// predefined vertex format
type VertexFormat =
    | Pos_TBN_Tex1_Bone4_Packed = 0

module VertexLayouts =
    // get a size of the input element
    let private getElementSize element =
        Formats.getSizeBits element / 8

    // build a layout from an array of (semantics, index, format) tuples
    let build components =
        // get component offsets
        let offsets = components |> Array.scan (fun acc (semantics, index, format) -> acc + getElementSize format) 0

        // get input elements
        let elements = components |> Array.mapi (fun i (semantics, index, format) -> InputElement(semantics, index, format, offsets.[i], 0))

        // build vertex layout
        { new VertexLayout with elements = elements and size = offsets.[offsets.Length - 1] }

    // prebuilt layouts
    let private Pos_TBN_Tex1_Bone4_Packed =
        build
            [|
                "POSITION", 0, Format.R16G16B16A16_UNorm;
                "NORMAL", 0, Format.R10G10B10A2_UNorm;
                "TANGENT", 0, Format.R10G10B10A2_UNorm;
                "TEXCOORD", 0, Format.R16G16_UNorm;
                "BONEINDICES", 0, Format.R8G8B8A8_UInt;
                "BONEWEIGHTS", 0, Format.R8G8B8A8_UNorm;
            |]

    // get a layout from the format
    let get format =
        match format with
        | VertexFormat.Pos_TBN_Tex1_Bone4_Packed -> Pos_TBN_Tex1_Bone4_Packed
        | _ -> failwith "Unknown format %A" format
