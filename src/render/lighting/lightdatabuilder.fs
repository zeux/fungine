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

// get bounding sphere radius for a set of points with a known center
let private getBoundingSphereRadius center (points: Vector3 array) =
    let fp = points |> Array.maxBy (fun p -> (center - p).LengthSquared)
    (center - fp).Length

// get bounding sphere for a set of points
let private getBoundingSphere (points: Vector3 array) =
    let center = (points |> Array.sum) / float32 points.Length
    center, getBoundingSphereRadius center points

// get bounding sphere for a set of points with a center on a known segment
let private getBoundingSphereWithCenterOnSegment (sbeg: Vector3) (send: Vector3) (points: Vector3 array) =
    let inline radius t = getBoundingSphereRadius $ Vector3.Lerp(sbeg, send, t) $ points

    let rec loop mint minr maxt maxr =
        if maxt - mint < 1e-4f then mint
        else
            let halft = (mint + maxt) / 2.f
            let halfr = radius halft
            if minr < maxr then
                loop mint minr halft halfr
            else
                loop halft halfr maxt maxr

    let t = loop 0.f (radius 0.f) 1.f (radius 1.f)

    radius t, Vector3.Lerp(sbeg, send, t)

// get stable bounding sphere for a frustum region
let private getStableBoundingSphere view projection znear zfar smView smSize =
    let clipToLight = Matrix44(smView * Matrix34.InverseAffine(view)) * Matrix44.Inverse(projection)

    let clipNear = getClipZ projection znear
    let clipFar = getClipZ projection zfar

    let points = Array.init 8 (fun i ->
        Vector4(
            (if i &&& 1 = 0 then -1.f else 1.f),
            (if i &&& 2 = 0 then -1.f else 1.f),
            (if i &&& 4 = 0 then clipNear else clipFar),
            1.f))

    let pointsView = points |> Array.map (fun p -> Matrix44.TransformPerspective(clipToLight, p))
    let pointsCenterBeg = Matrix44.TransformPerspective(clipToLight, Vector4(0.f, 0.f, clipNear, 1.f))
    let pointsCenterEnd = Matrix44.TransformPerspective(clipToLight, Vector4(0.f, 0.f, clipFar, 1.f))

    let sphereRadius, sphereCenter = getBoundingSphereWithCenterOnSegment pointsCenterBeg pointsCenterEnd pointsView

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
