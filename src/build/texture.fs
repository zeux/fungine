module Build.Texture

open System
open System.Runtime.InteropServices

module NvTextureTools =
    type Format =
        | RGB = 0
        | RGBA = 0
        | BC1 = 1
        | BC1a = 2
        | BC2 = 3
        | BC3 = 4
        | BC3n = 5 // R=1, G=y, B=0, A=x
        | BC4 = 6
        | BC5 = 7
        | BC6 = 10
        | BC7 = 11
        | RGBE = 12

    type Quality =
        | Fastest = 0
        | Normal = 1
        | Production = 2
        | Highest = 3

    type WrapMode =
        | Clamp = 0
        | Repeat = 1
        | Mirror = 2

    type MipmapFilter =
        | Box = 0
        | Triangle = 1
        | Kaiser = 2

    type ColorTransform =
        | None = 0
        | Linear = 1

    type RoundMode =
        | None = 0
        | ToNextPowerOfTwo = 1
        | ToNearestPowerOfTwo = 2
        | ToPreviousPowerOfTwo = 3

    type AlphaMode =
        | None = 0
        | Transparency = 1
        | Premultiplied = 2

    [<DllImport("nvtt")>]
    extern IntPtr nvttCreateInputOptions()

    [<DllImport("nvtt")>]
    extern void nvttDestroyInputOptions(IntPtr inputOptions)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsAlphaMode(IntPtr inputOptions, AlphaMode alphaMode)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsGamma(IntPtr inputOptions, float inputGamma, float outputGamma)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsWrapMode(IntPtr inputOptions, WrapMode mode)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsMipmapFilter(IntPtr inputOptions, MipmapFilter filter)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsMipmapGeneration(IntPtr inputOptions, bool enabled, int maxLevel)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsKaiserParameters(IntPtr inputOptions, float width, float alpha, float stretch)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsNormalMap(IntPtr inputOptions, bool b)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsConvertToNormalMap(IntPtr inputOptions, bool convert)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsHeightEvaluation(IntPtr inputOptions, float redScale, float greenScale, float blueScale, float alphaScale)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsNormalFilter(IntPtr inputOptions, float sm, float medium, float big, float large)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsNormalizeMipmaps(IntPtr inputOptions, bool b)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsColorTransform(IntPtr inputOptions, ColorTransform t);

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsLinearTransform(IntPtr inputOptions, int channel, float w0, float w1, float w2, float w3);

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsMaxExtents(IntPtr inputOptions, int dim)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsRoundMode(IntPtr inputOptions, RoundMode mode);

    [<DllImport("nvtt")>]
    extern IntPtr nvttCreateCompressionOptions()

    [<DllImport("nvtt")>]
    extern void nvttDestroyCompressionOptions(IntPtr compressionOptions)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsFormat(IntPtr compressionOptions, Format format)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsQuality(IntPtr compressionOptions, Quality quality)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsColorWeights(IntPtr compressionOptions, float red, float green, float blue, float alpha)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsPixelFormat(IntPtr compressionOptions, uint32 bitcount, uint32 rmask, uint32 gmask, uint32 bmask, uint32 amask)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsQuantization(IntPtr compressionOptions, bool colorDithering, bool alphaDithering, bool binaryAlpha, int alphaThreshold)

    type ErrorCallback = delegate of string -> unit

    [<DllImport("nvtt")>]
    extern bool nvttCompressFile(string source, string target, IntPtr inputOptions, IntPtr compressionOptions, ErrorCallback errorCallback)

let private compressInternal source target input compress =
    let callback = printf "Error building %s: %s" target
    let result = NvTextureTools.nvttCompressFile(source, target, input, compress, NvTextureTools.ErrorCallback(callback))

    NvTextureTools.nvttDestroyInputOptions(input)
    NvTextureTools.nvttDestroyCompressionOptions(compress)

    result

let build source target =
    let input = NvTextureTools.nvttCreateInputOptions()
    let compress = NvTextureTools.nvttCreateCompressionOptions()

    compressInternal source target input compress
