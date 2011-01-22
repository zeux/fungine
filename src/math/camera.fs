module Math.Camera

// get a perspective projection matrix
let projectionPerspective fov_y aspect znear zfar =
    let ys = 1.f / tan (fov_y / 2.f)
    let xs = ys / aspect
    let zs = zfar / (zfar - znear)

    Matrix44(xs,  0.f, 0.f, 0.f,
             0.f, ys,  0.f, 0.f,
             0.f, 0.f, zs, -znear * zs,
             0.f, 0.f, 1.f, 0.f)

// get a look-at matrix
let lookAt eye at desired_up =
    let view = Vector3.Normalize(at - eye)
    let side = Vector3.Normalize(Vector3.Cross(desired_up, view))
    let up = Vector3.Cross(view, side)

    Matrix34(Vector4(side, -Vector3.Dot(eye, side)),
             Vector4(up,   -Vector3.Dot(eye, up)),
             Vector4(view, -Vector3.Dot(eye, view)))