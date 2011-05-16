namespace BuildSystem

open System
open System.IO
open System.Net
open System.Security.Cryptography
open System.Threading

[<Struct>]
type Signature(high: uint64, low: uint64) =
    static let generator = new ThreadLocal<_>(fun () -> MD5.Create())
    static let encoding = Text.UTF8Encoding()

    // byte array ctor
    private new (data: byte[]) =
        assert (data.Length = 16)

        // input array is a 128-bit big-endian (network order) number
        let extract64 offset = BitConverter.ToInt64(data, offset) |> IPAddress.HostToNetworkOrder |> uint64
        Signature(extract64 0, extract64 8)

    // high/low part accessors
    member this.ValueHigh = high
    member this.ValueLow = low

    // compute signature from stream
    static member FromStream (data: Stream) = Signature(generator.Value.ComputeHash(data))

    // compute signature from file
    static member FromFile path =
        use stream = File.OpenRead(path)
        Signature.FromStream(stream)

    // compute signature from byte array
    static member FromBytes (data: byte[]) = Signature(generator.Value.ComputeHash(data))

    // compute signature from string
    static member FromString (data: string) = Signature.FromBytes(encoding.GetBytes(data))

    // combine several signatures into one
    static member Combine (list: Signature seq) =
        list
        |> Seq.toArray
        |> Array.collect (fun s ->
            let convert64 (value: uint64) = value |> int64 |> IPAddress.NetworkToHostOrder |> BitConverter.GetBytes
            Array.append (convert64 s.ValueHigh) (convert64 s.ValueLow))
        |> Signature.FromBytes
        
    // string conversion
    override this.ToString() = sprintf "%08x%08x" high low
