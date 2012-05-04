namespace Render

open System.Windows.Forms

open SharpDX.DXGI
open SharpDX.Direct3D11

// device
type Device(output: nativeint) =
    // create swap chain description
    let desc =
        new SwapChainDescription(
            BufferCount = 1,
            ModeDescription = ModeDescription(0, 0, Rational(0, 0), Format.R8G8B8A8_UNorm),
            IsWindowed = true,
            OutputHandle = output,
            SampleDescription = SampleDescription(1, 0),
            SwapEffect = SwapEffect.Discard,
            Usage = Usage.RenderTargetOutput,
            Flags = SwapChainFlags.AllowModeSwitch)

    // create device
    let (_, device, swapchain) = Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.None, desc)

    // disable Alt+Enter handling
    do swapchain.GetParent<Factory>().MakeWindowAssociation(output, WindowAssociationFlags.IgnoreAltEnter) |> ignore

    // get back buffer
    let getBackBuffer () =
        let resource = Texture2D.FromSwapChain<Texture2D>(swapchain, 0)
        new RenderTarget(resource, null, new RenderTargetView(device, resource), null)

    // back buffer
    let mutable backbuffer = getBackBuffer ()

    // get device
    member this.Device = device

    // get swapchain
    member this.SwapChain = swapchain
    
    // get backbuffer
    member this.BackBuffer = backbuffer

    // resize handler
    member this.OnSizeChanged () =
        // release old backbuffer (otherwise ResizeBuffers will fail)
        (backbuffer :> System.IDisposable).Dispose()

        // resize back/front buffers to the client area of the control
        swapchain.ResizeBuffers(1, 0, 0, swapchain.Description.ModeDescription.Format, swapchain.Description.Flags) |> ignore

        // recreate backbuffer
        backbuffer <- getBackBuffer ()