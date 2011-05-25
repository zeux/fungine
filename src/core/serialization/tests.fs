module Core.Serialization.Tests

open System.Collections.Generic

let roundtrip (obj: 'a) =
    use stream = new System.IO.MemoryStream()
    Core.Serialization.Save.toStream stream (box obj)
    stream.Position <- 0L
    (Core.Serialization.Load.fromStream stream (int stream.Length)) :?> 'a

let testRoundtripStructural obj =
    let rt = roundtrip obj
    assert (HashIdentity.Structural.Equals(rt, obj))

let testRoundtripStructuralSeq obj =
    let rt = roundtrip obj
    assert (Seq.forall2 (fun a b -> HashIdentity.Structural.Equals(a, b)) rt obj)

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
    testRoundtripStructural 'a'
    testRoundtripStructural 1.5m

// enums
type Enum8 =
    | Value = 1uy

type Enum64 =
    | Value = 1L

let testEnums () =
    testRoundtripStructural System.DayOfWeek.Wednesday
    testRoundtripStructural (System.UriComponents.Host ||| System.UriComponents.Port)
    testRoundtripStructural Enum8.Value
    testRoundtripStructural Enum64.Value

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

let testStringsUnicode () =
    testRoundtripStructural "ab\u0123\u101234cd"

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

    member this.Y = y
    override this.Sum = base.Sum + y

type int3(x: int, y: int, z: int) =
    inherit int2(x, y)

    member this.Z = z
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

// arrays
let testRoundtripStructuralArray (obj: 'T) =
    testRoundtripStructural ([||] : 'T array)
    testRoundtripStructural [| obj |]
    testRoundtripStructural [| obj; obj; obj |]

let testArraysPrimitive () =
    testRoundtripStructuralArray true
    testRoundtripStructuralArray 1
    testRoundtripStructuralArray 1y
    testRoundtripStructuralArray 1uy
    testRoundtripStructuralArray 1s
    testRoundtripStructuralArray 1us
    testRoundtripStructuralArray 1u
    testRoundtripStructuralArray 1L
    testRoundtripStructuralArray 1UL
    testRoundtripStructuralArray 1.0f
    testRoundtripStructuralArray 1.0
    testRoundtripStructuralArray 'a'
    testRoundtripStructuralArray 1.5m
    testRoundtripStructuralArray 1.5m

let testArraysEnum () =
    testRoundtripStructuralArray System.DayOfWeek.Wednesday
    testRoundtripStructuralArray (System.UriComponents.Host ||| System.UriComponents.Port)
    testRoundtripStructuralArray Enum8.Value
    testRoundtripStructuralArray Enum64.Value

let testArraysStruct () =
    testRoundtripStructuralArray (KeyValuePair(5, true))

let testArraysObject () =
    testRoundtripStructuralArray (System.Version("0.5"))

// generic collections
let testCollectionsList () =
    let list = List<string>()
    list.Add("hello")
    list.Add("world")
    testRoundtripStructuralSeq (list)
    testRoundtripStructuralSeq (List<int>())

// interfaces
let testInterfaceAggregation () =
    roundtrip ((HashIdentity.Structural<float> : IEqualityComparer<float>), 1) |> ignore

// fixup callback
type TestHandle() =
    let mutable data = 0

    member this.Data = data
    member private this.Fixup ctx = data <- data + 1

let testFixupCallback () =
    let h = TestHandle()
    h |> fun x -> assert (x.Data = 0)
    h |> roundtrip |> fun x -> assert (x.Data = 1)
    h |> roundtrip |> roundtrip |> fun x -> assert (x.Data = 2)