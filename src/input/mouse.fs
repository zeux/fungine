namespace Input

open System.Windows.Forms
open SlimDX.RawInput
open SlimDX.Multimedia

// mouse button
type MouseButton =
    | Left = 0
    | Right = 1
    | Middle = 2

// mouse state
type private MouseState =
    { axis_deltas: int array // relative movement for mouse axes (x, y, wheel)
      buttons_down: bool array // mouse button state
    }

    // clone
    member this.Clone () = { new MouseState with axis_deltas = Array.copy this.axis_deltas and buttons_down = Array.copy this.buttons_down }

    // default ctor
    static member Default = { new MouseState with axis_deltas = Array.create 3 0 and buttons_down = Array.create 3 false }

// mouse input device
type Mouse(control: Control) =
    // mouse states (current is copied to updated & reset on Update)
    let mutable updated = MouseState.Default
    let mutable current = MouseState.Default

    // grab mouse axis data via WM_INPUT (more precise, not related to mouse cursor position)
    do
        Device.RegisterDevice(UsagePage.Generic, UsageId.Mouse, DeviceFlags.None)
        Device.MouseInput.Add(fun args ->
            if control.Focused then
                [| args.X; args.Y; args.WheelDelta |] |> Array.iteri (fun i d -> current.axis_deltas.[i] <- current.axis_deltas.[i] + d))
    
    // grab mouse button data via WM_MOUSEDOWN and WM_MOUSEUP (WM_INPUT emits Down message on window drag/resize)
    do
        // convert MouseButtons enum to a list of MouseButton values
        let buttons list =
            [ if (list &&& MouseButtons.Left) <> MouseButtons.None then yield MouseButton.Left
              if (list &&& MouseButtons.Right) <> MouseButtons.None then yield MouseButton.Right
              if (list &&& MouseButtons.Middle) <> MouseButtons.None then yield MouseButton.Middle ]

        control.MouseDown.Add(fun args -> for b in buttons args.Button do current.buttons_down.[int b] <- true)
        control.MouseUp.Add(fun args -> for b in buttons args.Button do current.buttons_down.[int b] <- false)

    // reset data to default if focus is lost
    do
        control.LostFocus.Add(fun args -> current <- MouseState.Default)

    // update state
    member this.Update () =
        updated <- current.Clone()
        current <- { current with axis_deltas = MouseState.Default.axis_deltas }

    // mouse axes
    member this.AxisX = updated.axis_deltas.[0]
    member this.AxisY = updated.axis_deltas.[1]
    member this.AxisWheel = updated.axis_deltas.[2]

    // mouse buttons
    member this.ButtonDown (button: MouseButton) = updated.buttons_down.[int button]