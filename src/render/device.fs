module Render.Device

// device singleton
let mutable private device: SlimDX.Direct3D11.Device = null

// get device
let get () =
    assert (device <> null)
    device

// set device
let set value =
    assert (device = null && value <> null)
    device <- value