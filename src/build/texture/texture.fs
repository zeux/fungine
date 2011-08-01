module Build.Texture

open BuildSystem
open Build.NvTextureTools

open System.Collections.Generic

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

// set compression options from texture settings
let private setupOptions input compress (settings: Settings) =
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
        nvttSetInputOptionsAlphaMode(input, AlphaMode.Transparency)
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

// leak-safe handle type
type private Handle(handle, dtor) =
    member this.Value = handle

    interface System.IDisposable with
        override this.Dispose () = dtor(handle)

// convert the texture with specified options
let private build source target settings =
    use input = new Handle(nvttCreateInputOptions(), nvttDestroyInputOptions)
    use compress = new Handle(nvttCreateCompressionOptions(), nvttDestroyCompressionOptions)
    let callback = NvttErrorCallback(fun msg -> Output.echo (msg.Trim()))

    setupOptions input.Value compress.Value settings

    let result = nvttCompressFile(source, target, input.Value, compress.Value, callback)
    if not result then failwith "compression failed"

// texture setting database
let private settings = List<(string -> bool) * Settings>()

// add a file to texture settings db
let addSettings path =
    let doc = Core.Data.Load.fromFile path

    for (key, value) in doc.Pairs do
        // pattern is a or-separated list
        for part in key.Split([|'|'|]) do
            // add parsed elements to collection
            let r = Glob.matches (part.Trim())
            let s: Settings = Core.Data.Read.readNode doc value
            settings.Add((r, s))

// get settings from db for path
let getSettings path =
    // get settings that match pattern
    let ss = settings |> Seq.choose (fun (pattern, s) -> if pattern path then Some s else None) |> Seq.cache

    // select the last Some value, or default
    let select sel init = ss |> Seq.map sel |> Seq.fold (fun acc v -> if Option.isSome v then v else acc) (Some init)

    // return the final settings with value inheritance applied (rules are processed in order)
    { new Settings
        with profile = select (fun s -> s.profile) Profile.Generic
        and quality = select (fun s -> s.quality) Quality.Normal
        and format = select (fun s -> s.format) Format.Unknown
        and maxmip = select (fun s -> s.maxmip) -1 }
        
// texture builder object
let builder =
    { new Builder("Texture") with
        // build texture using database-specified settings
        override this.Build task =
            let settings = getSettings task.Sources.[0].Uid
            build task.Sources.[0].Path task.Targets.[0].Path settings
            None

        // version is a combination of static builder version and database-specified settings
        override this.Version task =
            let settings = getSettings task.Sources.[0].Uid
            System.String.Format("{0}({1},{2},{3},{4})", base.Version task, settings.profile.Value, settings.quality.Value, settings.format.Value, settings.maxmip.Value)
    }
