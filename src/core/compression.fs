module Core.Compression

// open System
// open System.Diagnostics
// open System.IO
open System.Runtime.InteropServices

// snappy.dll binding module
module private Snappy =
    [<DllImport("snappy", CallingConvention = CallingConvention.Cdecl)>]
    extern int snappy_compress(byte[] input, nativeint input_length, byte[] output, nativeint& compressed_length);

    [<DllImport("snappy", CallingConvention = CallingConvention.Cdecl)>]
    extern int snappy_uncompress(byte[] compressed, nativeint compressed_length, byte[] uncompressed, nativeint& uncompressed_length);

    [<DllImport("snappy", CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint snappy_max_compressed_length(nativeint source_length);

    [<DllImport("snappy", CallingConvention = CallingConvention.Cdecl)>]
    extern int snappy_uncompressed_length(byte[] compressed, nativeint compressed_length, nativeint& result);

// check result, raise exception on error
let private check error result =
    if result <> 0 then failwith error

// compress buffer
let compress (data: byte array) =
    // get maximum resulting length
    let mutable clength = Snappy.snappy_max_compressed_length(nativeint data.Length)

    // compress data
    let compressed = Array.zeroCreate (int clength)
    Snappy.snappy_compress(data, nativeint data.Length, compressed, &clength) |> check "Internal error during compression"
    assert (int clength <= compressed.Length)

    // return compressed chunk
    Array.sub compressed 0 (int clength)

// decompress buffer
let decompress (data: byte array) =
    // get original length
    let mutable length = 0n
    Snappy.snappy_uncompressed_length(data, nativeint data.Length, &length) |> check "Invalid compressed data"

    // decompress data
    let result = Array.zeroCreate (int length)
    Snappy.snappy_uncompress(data, nativeint data.Length, result, &length) |> check "Invalid compressed data"
    assert (int length = result.Length)

    // return decompressed chunk
    result