module Build.Geometry.PostTLOptimizerD3DX

open System.Runtime.InteropServices

[<DllImport("d3dx9_24")>]
extern int private D3DXOptimizeFaces([<MarshalAs(UnmanagedType.LPArray)>] int[] indices, int faceCount, int vertexCount, bool indices32, [<MarshalAs(UnmanagedType.LPArray)>] int[] remap)

// optimize indices for Post T&L cache efficiency using D3DX
let optimize indices =
    let vertexCount = 1 + Array.max indices
    let faceCount = indices.Length / 3

    let remap: int array = Array.zeroCreate faceCount

    let result = D3DXOptimizeFaces(indices, faceCount, vertexCount, true, remap)
    assert (result = 0)

    Array.init indices.Length (fun i -> indices.[remap.[i / 3] * 3 + i % 3])