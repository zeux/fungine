module Build.Texture

open BuildSystem
open Build.NvTextureTools
open Core.Data

open System.Collections.Generic
open System.Text.RegularExpressions

open SlimDX.Direct3D11

// texture compression profile
type Profile =
| Generic = 0
| Color = 1
| ColorAlpha = 2
| Normal = 3

// texture compression settings
type Settings =
    { mutable profile: Profile option
      mutable quality: Quality option
      mutable format: Format option
      mutable maxmip: int option
    }

// compress the texture with the specified options
let private compressInternal source target input compress =
    let callback (msg: string) = Output.echo (msg.Trim())
    let result = nvttCompressFile(source, target, input, compress, NvttErrorCallback(callback))

    nvttDestroyInputOptions(input)
    nvttDestroyCompressionOptions(compress)

    if not result then failwith "exit code %d" result

// convert the texture with default options
let private build source target (settings: Settings) =
    let input = nvttCreateInputOptions()
    let compress = nvttCreateCompressionOptions()

    // initial settings
    nvttSetInputOptionsGamma(input, 1.f, 1.f)

    // process profile
    match settings.profile.Value with
    | Profile.Generic ->
        nvttSetCompressionOptionsFormat(compress, Format.BC1)

    | Profile.Color ->
        nvttSetInputOptionsGamma(input, 2.2f, 2.2f)
        nvttSetCompressionOptionsFormat(compress, Format.BC1)

    | Profile.ColorAlpha ->
        nvttSetInputOptionsGamma(input, 2.2f, 2.2f)
        nvttSetCompressionOptionsFormat(compress, Format.BC3)

    | Profile.Normal ->
        nvttSetInputOptionsNormalMap(input, true)
        nvttSetInputOptionsNormalizeMipmaps(input, true)
        nvttSetCompressionOptionsFormat(compress, Format.BC5)

    | _ -> failwithf "Unknown profile %A" settings.profile.Value

    // setup quality and custom format
    nvttSetCompressionOptionsQuality(compress, settings.quality.Value)
    if settings.format.Value <> Format.Unknown then nvttSetCompressionOptionsFormat(compress, settings.format.Value)

    // setup mipmap generation options
    nvttSetInputOptionsMipmapGeneration(input, settings.maxmip.Value <> 0, settings.maxmip.Value)

    compressInternal source target input compress

// texture setting database
let private settings = List<Regex * Settings>()

// add a file to texture settings db
let addSettings path =
    let doc = Core.Data.Load.fromFile path

    for (key, value) in doc.Pairs do
        for part in key.Split([|'|'|]) do
            let pattern = part.Trim().ToLowerInvariant().Replace('\\', '/')
            let r = Regex(sprintf "^%s$" (Regex.Escape(pattern).Replace(@"\*\*", ".*").Replace(@"\*", "[^/.]*")))
            let s: Settings = Core.Data.Read.readNode doc value
            settings.Add((r, s))

// get settings from db for path
let getSettings path =
    let ss =
        settings
        |> Seq.choose (fun (pattern, s) ->
            if pattern.IsMatch path then Some s
            else None)

    let select sel init =
        ss
        |> Seq.map sel
        |> Seq.fold (fun acc v -> if Option.isSome v then v else acc) (Some init)

    { new Settings
        with profile = select (fun s -> s.profile) Profile.Generic
        and quality = select (fun s -> s.quality) Quality.Normal
        and format = select (fun s -> s.format) Format.Unknown
        and maxmip = select (fun s -> s.maxmip) -1 }
        
// texture builder object
let builder =
    { new Builder("Texture") with
        override this.Build task =
            let settings = getSettings task.Sources.[0].Uid
            build task.Sources.[0].Path task.Targets.[0].Path settings
            None

        override this.Version task =
            let settings = getSettings task.Sources.[0].Uid
            System.String.Format("{0}({1},{2},{3},{4})", base.Version task, settings.profile.Value, settings.quality.Value, settings.format.Value, settings.maxmip.Value)
    }