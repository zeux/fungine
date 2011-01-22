module Build.Geometry.PostTLOptimizerD3DX

open System.Runtime.InteropServices

[<DllImport("d3dx9_24")>]
extern int D3DXOptimizeFaces([<MarshalAs(UnmanagedType.LPArray)>] int[] indices, int face_count, int vertex_count, bool indices32, [<MarshalAs(UnmanagedType.LPArray)>] int[] remap)

// optimize indices for Post T&L cache efficiency using D3DX
let optimize indices =
    let vertex_count = 1 + Array.max indices
    let face_count = indices.Length / 3

    let remap: int array = Array.zeroCreate face_count

    let result = D3DXOptimizeFaces(indices, face_count, vertex_count, true, remap)
    assert (result = 0)

    Array.init indices.Length (fun i -> indices.[remap.[i / 3] * 3 + i % 3])