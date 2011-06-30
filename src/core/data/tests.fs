module Core.Data.Tests

open System.IO
open System.Text

open Core.Data

let parseString (data: string) =
    let bytes = Encoding.UTF8.GetBytes(data)
    use stream = new MemoryStream(bytes)
    Load.fromStream stream "stream"

let parseStringExpect data expected =
    let result = parseString data
    assert (result.Root = expected)

let parseStringExpectError (data: string) =
    try
        parseString data |> ignore
        assert (false)
    with e -> ()

let testSimpleArray () =
    parseStringExpect "foo = []" (Object [|"foo", Array [||]|])
    parseStringExpect "foo = [1]" (Object [|"foo", Array [|Value "1"|]|])
    parseStringExpect "foo = [1, 2]" (Object [|"foo", Array [|Value "1"; Value "2"|]|])

let testSimpleValue () =
    parseStringExpect "foo = 1" (Object [|"foo", Value "1"|])
    parseStringExpect "foo = abc" (Object [|"foo", Value "abc"|])
    parseStringExpect "foo = -0.45e+4" (Object [|"foo", Value "-0.45e+4"|])
    parseStringExpect "foo = \"bar\"" (Object [|"foo", Value "bar"|])

let testQuotedString () =
    parseStringExpect "\"foo bar\" = \"baz bar\"" (Object [|"foo bar", Value "baz bar"|])
    parseStringExpect "foo = \"a\r\n\tb\\\\\\\"\\\'c\"" (Object [|"foo", Value "a\r\n\tb\\\"\'c"|])

let testExcessiveComma () =
    parseStringExpect "foo = { bar = { baz = [1, 2,],}, }," (Object [|"foo", Object [|"bar", Object [|"baz", Array [|Value "1"; Value "2"|]|]|]|])

let testWrongComma () =
    parseStringExpectError "foo = { bar = 2,, }"
    parseStringExpectError "foo = { bar = 2 },,"
    parseStringExpectError ", foo = { bar = 2 }"
    parseStringExpectError "foo = { , bar = 2 }"
    parseStringExpectError "foo = [ 2,, ]"
    parseStringExpectError "foo = [ , 2 ]"

let testAbsentCommaError () =
    parseStringExpectError "foo = [1 2]"
    parseStringExpectError "foo = {x=1 y=2}"

let testAbsentCommaNewline () =
    parseStringExpect "foo = [1\n2]" (Object [|"foo", Array [|Value "1"; Value "2"|]|])
    parseStringExpect "foo = {x=1\ny=2}" (Object [|"foo", Object [|"x", Value "1"; "y", Value "2"|]|])
    parseStringExpect "foo = {x=1\n\t\t\n  \ny=2}" (Object [|"foo", Object [|"x", Value "1"; "y", Value "2"|]|])

let testCommaNewline () =
    parseStringExpect "foo = [1\n\n,\n\n2,\n\n]" (Object [|"foo", Array [|Value "1"; Value "2"|]|])

let testComment () =
    parseStringExpect "foo =\n\t2//hey" (Object [|"foo", Value "2"|])
    parseStringExpect "foo =\n\t[ 2//, -1\n, 3]" (Object [|"foo", Array [|Value "2"; Value "3"|]|])

let testPrematureEndGeneral () =
    parseStringExpectError "foo = {"
    parseStringExpectError "foo = { bar"
    parseStringExpectError "foo = { bar = "
    parseStringExpectError "foo = { bar = 5"

let testPrematureEndString () =
    parseStringExpectError "foo = \""
    parseStringExpectError "foo = \"123"
    parseStringExpectError "foo = \"123\\"
