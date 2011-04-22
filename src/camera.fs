module Camera

open System.Windows.Forms
open SlimDX.RawInput
open SlimDX.Multimedia

let dbg_yaw_speed = Core.DbgVar(0.01f, "camera/yaw speed")
let dbg_pitch_speed = Core.DbgVar(0.01f, "camera/pitch speed")
let dbg_movement_speed = Core.DbgVar(20.f, "camera/movement speed")

type CameraController() =
    let mutable yaw = 0.f
    let mutable pitch = 0.f
    let mutable active = false

    let mutable position = Vector3.Zero

    let pressed = Array.create 256 false

    do Device.RegisterDevice(UsagePage.Generic, UsageId.Mouse, DeviceFlags.None)
    do Device.RegisterDevice(UsagePage.Generic, UsageId.Keyboard, DeviceFlags.None)

    do Device.MouseInput.Add(fun args ->
        if (args.ButtonFlags &&& MouseButtonFlags.LeftDown) <> MouseButtonFlags.None then
            active <- true

        if (args.ButtonFlags &&& MouseButtonFlags.LeftUp) <> MouseButtonFlags.None then
            active <- false

        if active then
            yaw <- yaw + (float32 args.X) * dbg_yaw_speed.Value
            pitch <- pitch + (float32 args.Y) * dbg_pitch_speed.Value)

    do Device.KeyboardInput.Add(fun args ->
        if int args.Key < pressed.Length && (args.State = KeyState.Pressed || args.State = KeyState.Released) then
            pressed.[int args.Key] <- args.State = KeyState.Pressed)

    member this.Update dt =
        let offsets = 
            [| Keys.W, Vector3.UnitX
               Keys.S, -Vector3.UnitX
               Keys.A, -Vector3.UnitY
               Keys.D, Vector3.UnitY |]
            |> Array.choose (fun (key, offset) -> if pressed.[int key] then Some offset else None)

        if offsets.Length > 0 then
            let transform = this.Transform
            let speed = dbg_movement_speed.Value * (if pressed.[int Keys.ShiftKey] then 5.f else 1.f)

            position <- position + Matrix34.TransformDirection(transform, Array.sum offsets) * (speed * dt)

    member this.Transform =
        Matrix34.Translation(position) *
        Matrix34.RotationAxis(Vector3.UnitZ, yaw) *
        Matrix34.RotationAxis(Vector3.UnitY, pitch)

    member this.ViewMatrix =
        let transform = this.Transform

        let view = Matrix34.TransformDirection(transform, Vector3.UnitX)
        let up = Matrix34.TransformDirection(transform, Vector3.UnitZ)

        Math.Camera.lookAt position (position + view) up