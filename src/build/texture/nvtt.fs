module Build.NvTextureTools

open System.Runtime.InteropServices

type Format =
    | Unknown = -1
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

type NvttInputOptions = nativeint
type NvttCompressionOptions = nativeint

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern NvttInputOptions nvttCreateInputOptions()

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttDestroyInputOptions(NvttInputOptions)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsAlphaMode(NvttInputOptions, AlphaMode alphaMode)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsGamma(NvttInputOptions, float32 inputGamma, float32 outputGamma)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsWrapMode(NvttInputOptions, WrapMode mode)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsMipmapFilter(NvttInputOptions, MipmapFilter filter)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsMipmapGeneration(NvttInputOptions, bool enabled, int maxLevel)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsKaiserParameters(NvttInputOptions, float32 width, float32 alpha, float32 stretch)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsNormalMap(NvttInputOptions, bool b)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsConvertToNormalMap(NvttInputOptions, bool convert)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsHeightEvaluation(NvttInputOptions, float32 redScale, float32 greenScale, float32 blueScale, float32 alphaScale)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsNormalFilter(NvttInputOptions, float32 sm, float32 medium, float32 big, float32 large)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsNormalizeMipmaps(NvttInputOptions, bool b)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsMaxExtents(NvttInputOptions, int dim)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetInputOptionsRoundMode(NvttInputOptions, RoundMode mode);

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern NvttCompressionOptions nvttCreateCompressionOptions()

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttDestroyCompressionOptions(NvttCompressionOptions)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetCompressionOptionsFormat(NvttCompressionOptions, Format format)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetCompressionOptionsQuality(NvttCompressionOptions, Quality quality)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetCompressionOptionsColorWeights(NvttCompressionOptions, float32 red, float32 green, float32 blue, float32 alpha)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetCompressionOptionsPixelFormat(NvttCompressionOptions, uint32 bitcount, uint32 rmask, uint32 gmask, uint32 bmask, uint32 amask)

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern void nvttSetCompressionOptionsQuantization(NvttCompressionOptions, bool colorDithering, bool alphaDithering, bool binaryAlpha, int alphaThreshold)

type NvttErrorCallback = delegate of string -> unit

[<DllImport("nvtt", CallingConvention = CallingConvention.Cdecl)>]
extern bool nvttCompressFile(string source, string target, NvttInputOptions inputOptions, NvttCompressionOptions compressionOptions, NvttErrorCallback errorCallback)