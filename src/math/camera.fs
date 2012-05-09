module Math.Camera

// get an orthographic projection matrix
let projectionOrthoOffCenter left right bottom top znear zfar =
    let xs = 2.f / (right - left)
    let ys = 2.f / (top - bottom)
    let zs = 1.f / (zfar - znear)

    Matrix44(xs,  0.f, 0.f, -xs * (left + right),
             0.f, ys,  0.f, -ys * (top + bottom),
             0.f, 0.f, zs,  -zs * znear,
             0.f, 0.f, 0.f, 1.f)

// get a symmetrical orthographic projection matrix
let projectionOrtho width height znear zfar =
    let hw = width / 2.f
    let hh = height / 2.f
    projectionOrthoOffCenter -hw hw -hh hh znear zfar

// get a perspective projection matrix
let projectionPerspective fovY aspect znear zfar =
    let ys = 1.f / tan (fovY / 2.f)
    let xs = ys / aspect
    let zs = zfar / (zfar - znear)

    Matrix44(xs,  0.f, 0.f, 0.f,
             0.f, ys,  0.f, 0.f,
             0.f, 0.f, zs, -znear * zs,
             0.f, 0.f, 1.f, 0.f)

// get a look-at matrix
let lookAt eye at desiredUp =
    let view = Vector3.Normalize(at - eye)
    let side = Vector3.Normalize(Vector3.Cross(desiredUp, view))
    let up = Vector3.Cross(view, side)

    Matrix34(Vector4(side, -Vector3.Dot(eye, side)),
             Vector4(up,   -Vector3.Dot(eye, up)),
             Vector4(view, -Vector3.Dot(eye, view)))