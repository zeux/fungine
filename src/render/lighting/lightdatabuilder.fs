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

// get clip-space z
let private getClipZ projection z =
    Matrix44.TransformPerspective(projection, Vector4(0.f, 0.f, z, 1.f)).z

// get stable bounding sphere for a frustum region
let private getStableBoundingSphere view projection znear zfar smView smSize =
    let viewProjectionInverse = Matrix44.Inverse(projection * view)
    let points = Array.init 8 (fun i ->
        Vector4(
            (if i &&& 1 = 0 then -1.f else 1.f),
            (if i &&& 2 = 0 then -1.f else 1.f),
            getClipZ projection (if i &&& 4 = 0 then znear else zfar),
            1.f))

    let pointsView = points |> Array.map (fun p -> Matrix34.TransformPosition(smView, Matrix44.TransformPerspective(viewProjectionInverse, p)))
    let pointsMin = pointsView |> Array.reduce (fun a b -> Vector3.Minimize(a, b))
    let pointsMax = pointsView |> Array.reduce (fun a b -> Vector3.Maximize(a, b))

    let sphereCenter = Array.sum pointsView / 8.f
    let sphereRadius = pointsView |> Array.map (fun p -> (sphereCenter - p).Length) |> Array.max

    let texsize = sphereRadius * 2.f / float32 smSize
    let roundtex v = round (v / texsize) * texsize

    roundtex sphereRadius, Vector3(roundtex sphereCenter.x, roundtex sphereCenter.y, roundtex sphereCenter.z)

// get cascade split distance
let private getSplitDistance znear zfar llcoeff split splitCount =
    let coeff = float32 split / float32 splitCount

    let logDistance = znear * ((zfar / znear) ** coeff)
    let linDistance = znear + (zfar - znear) * coeff

    logDistance + (linDistance - logDistance) * llcoeff

let dbgCascadeCoeff = Core.DbgVar(0.2f, "shadows/cascade split coeff")

// get shadow data for a light
let private getShadowData light eyeView eyeProjection =
    match light with
    | DirectionalLight l ->
        let smSize = 1024
        let znear = 0.1f
        let zfar = 1000.f
        let splits = 4

        let view = Math.Camera.lookAt Vector3.Zero l.direction (if abs l.direction.x < 0.7f then Vector3.UnitX else Vector3.UnitY)
        let tr, tc = getStableBoundingSphere eyeView eyeProjection znear zfar view smSize
        let proj = Math.Camera.projectionOrthoOffCenter (tc.x - tr) (tc.x + tr) (tc.y - tr) (tc.y + tr) (tc.z - tr) (tc.z + tr)

        let cascades =
            Array.init splits (fun split ->
                let cnear = getSplitDistance znear zfar dbgCascadeCoeff.Value split splits
                let cfar = getSplitDistance znear zfar dbgCascadeCoeff.Value (split + 1) splits
                let r, c = getStableBoundingSphere eyeView eyeProjection cnear cfar view smSize

                cfar, Vector2(tr / r, tr / r), (tc.xy - c.xy) / r)

        Some (smSize, (proj * Matrix44(view)), cascades)
    | PointLight l ->
        None
    | SpotLight l ->
        let view = Math.Camera.lookAt l.position (l.position + l.direction) (if abs l.direction.x < 0.7f then Vector3.UnitX else Vector3.UnitY)
        let proj = Math.Camera.projectionPerspective (l.outerAngle * 2.f) 1.f 0.01f l.radius
        Some (512, (proj * Matrix44(view)), [|0.f, Vector2(1.f, 1.f), Vector2.Zero|])

// get render data for a light
let private getRenderData light shadow =
    match light with
    | DirectionalLight l -> LightData(LightType.Directional, Vector3.Zero, l.direction, 0.f, 0.f, 0.f, l.color, l.intensity, shadow)
    | PointLight l -> LightData(LightType.Point, l.position, Vector3.Zero, l.radius, 0.f, 0.f, l.color, l.intensity, shadow)
    | SpotLight l -> LightData(LightType.Spot, l.position, l.direction, l.radius, cos l.outerAngle, cos l.innerAngle, l.color, l.intensity, shadow)

// build light data
let build lights shadowAtlasWidth shadowAtlasHeight view projection =
    // get culling info
    let cullData = lights |> Array.map getCullData

    // get shadowing info
    let packer = RectPacker(shadowAtlasWidth, shadowAtlasHeight)

    let shadowCascadeDummy = LightShadowCascade(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero)
    let shadowDataDummy = LightShadowData(Matrix44.Identity, Vector4(infinityf, infinityf, infinityf, infinityf), [|shadowCascadeDummy|])
    let shadowData =
        lights
        |> Array.map (fun l -> getShadowData l view projection)
        |> Array.map (function
            | Some (size, matrix, cascades) ->
                let cascadeInfo =
                    cascades |> Array.map (fun (distance, transformScale, transformOffset) ->
                        match packer.Pack(size, size) with
                        | Some (x, y) ->
                            let offset = Vector2(float32 x / float32 shadowAtlasWidth, float32 y / float32 shadowAtlasHeight)
                            let scale = Vector2(float32 size / float32 shadowAtlasWidth, float32 size / float32 shadowAtlasHeight)
                            distance, LightShadowCascade(transformScale, transformOffset, scale, offset)
                        | None -> infinityf, shadowCascadeDummy)

                let dist i = if i < cascadeInfo.Length then fst cascadeInfo.[i] else infinityf
                LightShadowData(matrix, Vector4(dist 0, dist 1, dist 2, dist 3), cascadeInfo |> Array.map snd)
            | None -> shadowDataDummy)

    // get render info
    let lightData = Array.map2 getRenderData lights shadowData

    // return everything
    cullData, lightData