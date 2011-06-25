module Camera

open Input

let dbg_yaw_speed = Core.DbgVar(0.01f, "camera/yaw speed")
let dbg_pitch_speed = Core.DbgVar(0.01f, "camera/pitch speed")
let dbg_movement_speed = Core.DbgVar(5.f, "camera/movement speed")

type CameraController(mouse: Mouse, keyboard: Keyboard) =
    let mutable yaw = 0.f
    let mutable pitch = 0.f

    let mutable position = Vector3.Zero

    member this.Update dt =
        if mouse.ButtonDown MouseButton.Left then
            yaw <- yaw + (float32 mouse.AxisX) * dbg_yaw_speed.Value
            pitch <- pitch + (float32 mouse.AxisY) * dbg_pitch_speed.Value

        let offsets = 
            [| Key.W, Vector3.UnitX
               Key.S, -Vector3.UnitX
               Key.A, -Vector3.UnitY
               Key.D, Vector3.UnitY |]
            |> Array.choose (fun (key, offset) -> if keyboard.KeyDown key then Some offset else None)

        if offsets.Length > 0 then
            let transform = this.Transform
            let speed = dbg_movement_speed.Value * (if keyboard.KeyDown Key.ShiftKey then 5.f else 1.f)

            position <- position + Matrix34.TransformDirection(transform, Array.sum offsets) * (speed * dt)

    member this.Position
        with get () = position
        and set value = position <- value

    member this.Yaw
        with get () = yaw
        and set value = yaw <- value

    member this.Pitch
        with get () = pitch
        and set value = pitch <- value

    member this.Transform =
        Matrix34.Translation(position) *
        Matrix34.RotationAxis(Vector3.UnitZ, yaw) *
        Matrix34.RotationAxis(Vector3.UnitY, pitch)

    member this.ViewMatrix =
        let transform = this.Transform

        let view = Matrix34.TransformDirection(transform, Vector3.UnitX)
        let up = Matrix34.TransformDirection(transform, Vector3.UnitZ)

        Math.Camera.lookAt position (position + view) up