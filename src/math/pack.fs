module Math.Pack

let inline private clamp value left right =
    min right (max left value)

let packFloatUNorm value bits =
    let scale = (1 <<< bits) - 1
    uint32 ((clamp value 0.f 1.f) * (float32 scale) + 0.5f)

let packFloatSNorm value bits =
    let scale = (1 <<< bits) - 1
    let signed = int ((clamp value -1.f 1.f) * (float32 scale) * 0.5f + 0.5f)
    (uint32 signed) &&& (uint32 scale)

let packDirectionR8G8B8 (v: Vector3) =
    let x = packFloatSNorm v.X 8
    let y = packFloatSNorm v.Y 8
    let z = packFloatSNorm v.Z 8
    x ||| (y <<< 8) ||| (z <<< 16)