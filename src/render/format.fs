namespace Render

// format for render data (vertices, pixels, etc.)
type Format = SharpDX.DXGI.Format

module Formats =
    // get a size of the format (bits per element)
    let getSizeBits format =
        SharpDX.DXGI.FormatHelper.SizeOfInBits(format)

    // is the format a depth/stencil one
    let isDepth format =
        match format with
        | Format.D32_Float_S8X24_UInt
        | Format.D32_Float
        | Format.D24_UNorm_S8_UInt
        | Format.D16_UNorm
            -> true
        | _ -> false

    // is the format a block compressed one
    let isBlockCompressed format =
        match format with
        | Format.BC1_Typeless | Format.BC1_UNorm | Format.BC1_UNorm_SRgb
        | Format.BC2_Typeless | Format.BC2_UNorm | Format.BC2_UNorm_SRgb
        | Format.BC3_Typeless | Format.BC3_UNorm | Format.BC3_UNorm_SRgb
        | Format.BC4_Typeless | Format.BC4_UNorm | Format.BC4_SNorm
        | Format.BC5_Typeless | Format.BC5_UNorm | Format.BC5_SNorm
        | Format.BC6H_Typeless | Format.BC6H_Uf16 | Format.BC6H_Sf16
        | Format.BC7_Typeless | Format.BC7_UNorm | Format.BC7_UNorm_SRgb
            -> true
        | _ -> false
