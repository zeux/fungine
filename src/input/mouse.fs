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
    { axisDeltas: int array // relative movement for mouse axes (x, y, wheel)
      cursorPosition: int array // mouse cursor position
      buttonsDown: bool array // mouse button state
    }

    // clone
    member this.Clone () =
        { new MouseState
            with axisDeltas = Array.copy this.axisDeltas
            and cursorPosition = Array.copy this.cursorPosition
            and buttonsDown = Array.copy this.buttonsDown }

    // default ctor
    static member Default =
        { new MouseState
            with axisDeltas = Array.create 3 0
            and cursorPosition = Array.create 2 0
            and buttonsDown = Array.create 3 false }

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
                [| args.X; args.Y; args.WheelDelta |] |> Array.iteri (fun i d -> current.axisDeltas.[i] <- current.axisDeltas.[i] + d))
    
    // grab mouse button data via WM_MOUSEDOWN and WM_MOUSEUP (WM_INPUT emits Down message on window drag/resize)
    do
        // convert MouseButtons enum to a list of MouseButton values
        let buttons list =
            [ if (list &&& MouseButtons.Left) <> MouseButtons.None then yield MouseButton.Left
              if (list &&& MouseButtons.Right) <> MouseButtons.None then yield MouseButton.Right
              if (list &&& MouseButtons.Middle) <> MouseButtons.None then yield MouseButton.Middle ]

        control.MouseDown.Add(fun args -> for b in buttons args.Button do current.buttonsDown.[int b] <- true)
        control.MouseUp.Add(fun args -> for b in buttons args.Button do current.buttonsDown.[int b] <- false)
        control.MouseMove.Add(fun args -> current.cursorPosition.[0] <- args.X; current.cursorPosition.[1] <- args.Y)

    // reset data to default if focus is lost
    do
        control.LostFocus.Add(fun args -> current <- MouseState.Default)

    // update state
    member this.Update () =
        updated <- current.Clone()
        current <- { current with axisDeltas = MouseState.Default.axisDeltas }

    // mouse axes
    member this.AxisX = updated.axisDeltas.[0]
    member this.AxisY = updated.axisDeltas.[1]
    member this.AxisWheel = updated.axisDeltas.[2]

    // mouse cursor
    member this.CursorX = updated.cursorPosition.[0]
    member this.CursorY = updated.cursorPosition.[1]

    // mouse buttons
    member this.ButtonDown (button: MouseButton) = updated.buttonsDown.[int button]