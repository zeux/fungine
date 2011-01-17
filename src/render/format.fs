namespace Render

type Format = SlimDX.DXGI.Format

module Formats =
    // get a size of the format (bits per element)
    let getSizeBits format =
        match format with
        // RGBA32
        | Format.R32G32B32A32_Typeless | Format.R32G32B32A32_Float | Format.R32G32B32A32_UInt | Format.R32G32B32A32_SInt -> 128
        // RGB32
        | Format.R32G32B32_Typeless | Format.R32G32B32_Float | Format.R32G32B32_UInt | Format.R32G32B32_SInt -> 96
        // RGBA16
        | Format.R16G16B16A16_Typeless | Format.R16G16B16A16_Float | Format.R16G16B16A16_UNorm | Format.R16G16B16A16_UInt | Format.R16G16B16A16_SNorm | Format.R16G16B16A16_SInt -> 64
        // RG32
        | Format.R32G32_Typeless | Format.R32G32_Float | Format.R32G32_UInt | Format.R32G32_SInt -> 64
        // D32S8
        | Format.R32G8X24_Typeless | Format.D32_Float_S8X24_UInt | Format.R32_Float_X8X24_Typeless | Format.X32_Typeless_G8X24_UInt -> 64
        // RGB10A2
        | Format.R10G10B10A2_Typeless | Format.R10G10B10A2_UNorm | Format.R10G10B10A2_UInt -> 32
        // R11G11B10
        | Format.R11G11B10_Float -> 32
        // RGBA8
        | Format.R8G8B8A8_Typeless | Format.R8G8B8A8_UNorm | Format.R8G8B8A8_UNorm_SRGB | Format.R8G8B8A8_UInt | Format.R8G8B8A8_SNorm | Format.R8G8B8A8_SInt -> 32
        // RG16
        | Format.R16G16_Typeless | Format.R16G16_Float | Format.R16G16_UNorm | Format.R16G16_UInt | Format.R16G16_SNorm | Format.R16G16_SInt -> 32
        // R32, D32
        | Format.R32_Typeless | Format.D32_Float | Format.R32_Float | Format.R32_UInt | Format.R32_SInt -> 32
        // D24S8
        | Format.R24G8_Typeless | Format.D24_UNorm_S8_UInt | Format.R24_UNorm_X8_Typeless | Format.X24_Typeless_G8_UInt -> 32
        // RG8
        | Format.R8G8_Typeless | Format.R8G8_UNorm | Format.R8G8_UInt | Format.R8G8_SNorm | Format.R8G8_SInt -> 16
        // R16, D16
        | Format.R16_Typeless | Format.R16_Float | Format.D16_UNorm | Format.R16_UNorm | Format.R16_UInt | Format.R16_SNorm | Format.R16_SInt -> 16
        // R8
        | Format.R8_Typeless | Format.R8_UNorm | Format.R8_UInt | Format.R8_SNorm | Format.R8_SInt -> 8
        // A8
        | Format.A8_UNorm -> 8
        // R1
        | Format.R1_UNorm -> 1
        // RGB9E5
        | Format.R9G9B9E5_SharedExp -> 8
        // RG8_BG8 - 4 bytes for 2 pixels
        | Format.R8G8_B8G8_UNorm | Format.G8R8_G8B8_UNorm -> 16
        // BC1 - 8 bytes for 16 pixels
        | Format.BC1_Typeless | Format.BC1_UNorm | Format.BC1_UNorm_SRGB -> 4
        // BC2/3 - 16 bytes for 16 pixels
        | Format.BC2_Typeless | Format.BC2_UNorm | Format.BC2_UNorm_SRGB | Format.BC3_Typeless | Format.BC3_UNorm | Format.BC3_UNorm_SRGB -> 8
        // BC4 - 8 bytes for 16 pixels (one channel)
        | Format.BC4_Typeless | Format.BC4_UNorm | Format.BC4_SNorm -> 4
        // BC5 - 16 bytes for 16 pixels (two channels)
        | Format.BC5_Typeless | Format.BC5_UNorm | Format.BC5_SNorm -> 8
        // BC6/7 - 16 bytes for 16 pixels
        | Format.BC6_Typeless | Format.BC6_UFloat16 | Format.BC6_SFloat16 | Format.BC7_Typeless | Format.BC7_UNorm | Format.BC7_UNorm_SRGB -> 8
        // RGB 16-bit
        | Format.B5G6R5_UNorm | Format.B5G5R5A1_UNorm -> 16
        // BGRA8
        | Format.B8G8R8A8_UNorm | Format.B8G8R8X8_UNorm | Format.B8G8R8A8_Typeless | Format.B8G8R8A8_UNorm_SRGB | Format.B8G8R8X8_Typeless | Format.B8G8R8X8_UNorm_SRGB -> 8
        // RGB10A2
        | Format.R10G10B10_XR_Bias_A2_UNorm -> 8
        // Unknown
        | _ -> failwith "Unknown format"

    // is the format a depth/stencil one
    let isDepth format =
        match format with
        | Format.D32_Float_S8X24_UInt
        | Format.D32_Float
        | Format.D24_UNorm_S8_UInt
        | Format.D16_UNorm
            -> true
        | _ -> false
