module Render.Lighting.LightDataBuilder

// utility class for dumb rectangle packing
type private RectPacker(maxWidth, maxHeight) =
    let mutable x = 0
    let mutable y = 0
    let mutable nexty = 0

    member this.Pack(width, height) =
        // switch to next line
        if x + width > maxWidth then
            x <- 0
            y <- nexty

        // check if the rectangle fits in both dimensions
        if x + width <= maxWidth && y + height <= maxHeight then
            nexty <- max nexty (y + height)
            x <- x + width
            Some (x - width, y)
        else
            None

// get cull data for a light
let private getCullData light =
    match light with
    | DirectionalLight l -> LightCullData(LightType.Directional, Vector3.Zero, 0.f)
    | PointLight l -> LightCullData(LightType.Point, l.position, l.radius)
    | SpotLight l -> LightCullData(LightType.Spot, l.position, l.radius)

// get shadow data for a light
let private getShadowData light =
    match light with
    | DirectionalLight l ->
        let view = Math.Camera.lookAt Vector3.Zero l.direction (if abs l.direction.x < 0.7f then Vector3.UnitX else Vector3.UnitY)
        let proj = Math.Matrix44(Math.Matrix34.Scaling(1.f / 100.f) * Math.Matrix34.Translation(0.f, 0.f, -100.f))
        Some (1024, (proj * Matrix44(view)))
    | PointLight l ->
        None
    | SpotLight l ->
        let view = Math.Camera.lookAt l.position (l.position + l.direction) (if abs l.direction.x < 0.7f then Vector3.UnitX else Vector3.UnitY)
        let proj = Math.Camera.projectionPerspective (l.outerAngle * 2.f) 1.f 0.01f l.radius
        Some (512, (proj * Matrix44(view)))

// get render data for a light
let private getRenderData light shadow =
    match light with
    | DirectionalLight l -> LightData(LightType.Directional, Vector3.Zero, l.direction, 0.f, 0.f, 0.f, l.color, l.intensity, shadow)
    | PointLight l -> LightData(LightType.Point, l.position, Vector3.Zero, l.radius, 0.f, 0.f, l.color, l.intensity, shadow)
    | SpotLight l -> LightData(LightType.Spot, l.position, l.direction, l.radius, cos l.outerAngle, cos l.innerAngle, l.color, l.intensity, shadow)

// build light data
let build lights shadowAtlasWidth shadowAtlasHeight =
    // get culling info
    let cullData = lights |> Array.map getCullData

    // get shadowing info
    let packer = RectPacker(shadowAtlasWidth, shadowAtlasHeight)

    let shadowDataDummy = LightShadowData(Matrix44.Identity, Vector2.Zero, Vector2.Zero)
    let shadowData =
        lights
        |> Array.map getShadowData
        |> Array.map (function
            | Some (size, matrix) ->
                match packer.Pack(size, size) with
                | Some (x, y) ->
                    let offset = Vector2(float32 x / float32 shadowAtlasWidth, float32 y / float32 shadowAtlasHeight)
                    let scale = Vector2(float32 size / float32 shadowAtlasWidth, float32 size / float32 shadowAtlasHeight)
                    LightShadowData(matrix, scale, offset)
                | None -> shadowDataDummy
            | None -> shadowDataDummy)

    // get render info
    let lightData = Array.map2 getRenderData lights shadowData

    // return everything
    cullData, lightData