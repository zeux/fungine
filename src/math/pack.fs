module Math.Pack

let inline private clamp value left right =
    min right (max left value)

let inline private round value =
    int (value + (if value > 0.f then 0.5f else -0.5f))

let packFloatUNorm value bits =
    let scale = (1 <<< bits) - 1
    uint32 (round ((clamp value 0.f 1.f) * (float32 scale)))

let packFloatSNorm value bits =
    let scale = (1 <<< (bits - 1)) - 1
    let mask = (1u <<< bits) - 1u
    let result = round ((clamp value -1.f 1.f) * (float32 scale))
    (uint32 result) &&& mask

let packDirectionSNorm (v: MathTypes.Vector3) bits =
    let x = packFloatSNorm v.x bits
    let y = packFloatSNorm v.y bits
    let z = packFloatSNorm v.z bits
    x ||| (y <<< bits) ||| (z <<< (2 * bits))

let packDirectionUNorm (v: MathTypes.Vector3) bits =
    let x = packFloatUNorm (v.x * 0.5f + 0.5f) bits
    let y = packFloatUNorm (v.y * 0.5f + 0.5f) bits
    let z = packFloatUNorm (v.z * 0.5f + 0.5f) bits
    x ||| (y <<< bits) ||| (z <<< (2 * bits))

let packDirectionUnnormalized (v: MathTypes.Vector3) bits offset =
    // round x to nearest integer
    let inline round x = int (x + (if x > 0.f then 0.5f else -0.5f))

    // get rounding error for positive x
    let inline rounderr x = abs (x - float (int (x + 0.5)))

    // normalize by largest component
    let axis = if abs v.x > abs v.y && abs v.x > abs v.z then 0 else if abs v.y > abs v.x then 1 else 2
    let n1 = v / abs v.[axis]

    // select two other components (abs values)
    let n1_0 = abs (float (if axis = 0 then n1.y else n1.x))
    let n1_1 = abs (float (if axis = 2 then n1.y else n1.z))

    // approximate both components with integer ratios with a common denominator
    let mutable bestv = 0.0
    let mutable beste = infinity

    for v in 1 <<< (bits - 2) .. (1 <<< (bits - 1)) - 1 do
        let fv = float v
        let e = ((rounderr (n1_0 * fv)) + (rounderr (n1_1 * fv))) / fv
        
        if e < beste then
            beste <- e
            bestv <- fv

    // pack values together
    let mask = (1u <<< bits) - 1u
    let x = uint32 (round (n1.x * float32 bestv) + offset) &&& mask
    let y = uint32 (round (n1.y * float32 bestv) + offset) &&& mask
    let z = uint32 (round (n1.z * float32 bestv) + offset) &&& mask
    
    x ||| (y <<< bits) ||| (z <<< (2 * bits))

let packDirectionUnnormalizedSigned (v: MathTypes.Vector3) bits =
    packDirectionUnnormalized v bits 0

let packDirectionUnnormalizedUnsigned (v: MathTypes.Vector3) bits =
    packDirectionUnnormalized v bits (1 <<< (bits - 1))