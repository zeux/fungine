namespace Input

open System.Windows.Forms

// keyboard key
type Key = Keys

// keyboard state
type private KeyboardState =
    { keys_down: bool array // key state
    }

    // clone
    member this.Clone () = { new KeyboardState with keys_down = Array.copy this.keys_down }

    // default ctor
    static member Default = { new KeyboardState with keys_down = Array.create 256 false }

// keyboard input device
type Keyboard(control: Control) =
    // keyboard states (current is copied to updated & reset on Update)
    let mutable updated = KeyboardState.Default
    let mutable current = KeyboardState.Default

    // grab key data via WM_KEYDOWN and WM_KEYUP
    do
        control.KeyDown.Add(fun args -> current.keys_down.[int args.KeyCode] <- true)
        control.KeyUp.Add(fun args -> current.keys_down.[int args.KeyCode] <- false)

    // reset data to default if focus is lost
    do
        control.LostFocus.Add(fun args -> current <- KeyboardState.Default)

    // update state
    member this.Update () =
        updated <- current.Clone()

    // key states
    member this.KeyDown (key: Key) = updated.keys_down.[int key]