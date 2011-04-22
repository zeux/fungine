module Build.Texture

open System
open System.Runtime.InteropServices

// nvtt.dll binding module
module private NvTextureTools =
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

    type RoundMode =
        | None = 0
        | ToNextPowerOfTwo = 1
        | ToNearestPowerOfTwo = 2
        | ToPreviousPowerOfTwo = 3

    type AlphaMode =
        | None = 0
        | Transparency = 1
        | Premultiplied = 2

    type NvttInputOptions = IntPtr
    type NvttCompressionOptions = IntPtr

    [<DllImport("nvtt")>]
    extern NvttInputOptions nvttCreateInputOptions()

    [<DllImport("nvtt")>]
    extern void nvttDestroyInputOptions(NvttInputOptions)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsAlphaMode(NvttInputOptions, AlphaMode alphaMode)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsGamma(NvttInputOptions, float inputGamma, float outputGamma)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsWrapMode(NvttInputOptions, WrapMode mode)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsMipmapFilter(NvttInputOptions, MipmapFilter filter)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsMipmapGeneration(NvttInputOptions, bool enabled, int maxLevel)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsKaiserParameters(NvttInputOptions, float width, float alpha, float stretch)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsNormalMap(NvttInputOptions, bool b)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsConvertToNormalMap(NvttInputOptions, bool convert)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsHeightEvaluation(NvttInputOptions, float redScale, float greenScale, float blueScale, float alphaScale)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsNormalFilter(NvttInputOptions, float sm, float medium, float big, float large)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsNormalizeMipmaps(NvttInputOptions, bool b)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsMaxExtents(NvttInputOptions, int dim)

    [<DllImport("nvtt")>]
    extern void nvttSetInputOptionsRoundMode(NvttInputOptions, RoundMode mode);

    [<DllImport("nvtt")>]
    extern NvttCompressionOptions nvttCreateCompressionOptions()

    [<DllImport("nvtt")>]
    extern void nvttDestroyCompressionOptions(NvttCompressionOptions)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsFormat(NvttCompressionOptions, Format format)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsQuality(NvttCompressionOptions, Quality quality)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsColorWeights(NvttCompressionOptions, float red, float green, float blue, float alpha)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsPixelFormat(NvttCompressionOptions, uint32 bitcount, uint32 rmask, uint32 gmask, uint32 bmask, uint32 amask)

    [<DllImport("nvtt")>]
    extern void nvttSetCompressionOptionsQuantization(NvttCompressionOptions, bool colorDithering, bool alphaDithering, bool binaryAlpha, int alphaThreshold)

    type NvttErrorCallback = delegate of string -> unit

    [<DllImport("nvtt")>]
    extern bool nvttCompressFile(string source, string target, NvttInputOptions inputOptions, NvttCompressionOptions compressionOptions, NvttErrorCallback errorCallback)

open NvTextureTools

// compress the texture with the specified options
let private compressInternal source target input compress =
    let callback = printf "Build.Texture[%s]: error %s" source
    let result = nvttCompressFile(source, target, input, compress, NvttErrorCallback(callback))

    nvttDestroyInputOptions(input)
    nvttDestroyCompressionOptions(compress)

    result

// convert the texture with default options
let build source target =
    let input = nvttCreateInputOptions()
    let compress = nvttCreateCompressionOptions()

    nvttSetInputOptionsAlphaMode(input, AlphaMode.Transparency)
    nvttSetCompressionOptionsFormat(compress, Format.BC1a)
    nvttSetCompressionOptionsQuantization(compress, false, false, true, 128)

    compressInternal source target input compress
