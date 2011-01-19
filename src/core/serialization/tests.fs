module Core.Serialization.Tests

let roundtrip (obj: 'a) =
    use stream = new System.IO.MemoryStream()
    Core.Serialization.Save.toStream stream (box obj)
    stream.Position <- 0L
    (Core.Serialization.Load.fromStream stream) :?> 'a

let testRoundtripStructural obj =
    let rt = roundtrip obj
    assert (HashIdentity.Structural.Equals(rt, obj))

// primitive types
let testPrimitive () =
    testRoundtripStructural true
    testRoundtripStructural 1
    testRoundtripStructural 1y
    testRoundtripStructural 1uy
    testRoundtripStructural 1s
    testRoundtripStructural 1us
    testRoundtripStructural 1u
    testRoundtripStructural 1L
    testRoundtripStructural 1UL
    testRoundtripStructural 1.0f
    testRoundtripStructural 1.0

// enums
let testEnums () =
    testRoundtripStructural System.DayOfWeek.Wednesday
    testRoundtripStructural (System.UriComponents.Host ||| System.UriComponents.Port)

// built-in aggregate types
let testBuiltinAggregates () =
    testRoundtripStructural (1, 2, 3)
    testRoundtripStructural [1; 2; 3]
    testRoundtripStructural [|1; 2; 3|]

// other built-in types
let testBuiltinOther () =
    testRoundtripStructural (Some 1)
    testRoundtripStructural (ref 1)

// strings
let testStrings () =
    testRoundtripStructural ""
    testRoundtripStructural "abc"
    testRoundtripStructural "abc\0 def"

// structures
let testStructs () =
    testRoundtripStructural (System.Collections.Generic.KeyValuePair(5, true))

// simple objects
let testObjectsSimple () =
    testRoundtripStructural (System.Version("0.5"))

// object inheritance
type int1(x: int) =
    abstract Sum: int with get

    member this.X = x
    override this.Sum = x

type int2(x: int, y: int) =
    inherit int1(x)

    member x.Y = y
    override this.Sum = base.Sum + y

type int3(x: int, y: int, z: int) =
    inherit int2(x, y)

    member x.Z = z
    override this.Sum = base.Sum + z

let testObjectsInheritance () =
    assert ((roundtrip (int1(1))).Sum = 1)
    assert ((roundtrip (int2(1, 2))).Sum = 3)
    assert ((roundtrip (int3(1, 2, 3))).Sum = 6)
    assert ((roundtrip (int3(1, 2, 3) :> int1)).Sum = 6)

// object references
let testObjectsReferences () =
    let b1 = box 1
    let b2 = box 2.f

    let d1 = [|b1; b2; b1; b1|]
    let d2 = roundtrip d1

    assert (Array.forall2 (fun x y -> x = y) d1 d2)

let testObjectsReferencesCircular () =
    let o1 = ref null
    let o2 = ref (box o1)
    let o3 = ref (box o2)

    o1 := box o3

    let r3 = roundtrip o3
    let r2 = unbox !r3
    let r1 = unbox !r2

    assert (System.Object.ReferenceEquals(!r1, box r3))