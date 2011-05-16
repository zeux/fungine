module BuildSystem.Output

open System
open System.Text

// output options
[<Flags>]
type Options =
    | None = 0
    | DebugExplain = 1              // explain why the target is being rebuilt
    | DebugFileSignature = 2        // trace file signature calculation

// current output options
let mutable options = Options.None

// print message to the output
let echo (s: string) = Console.WriteLine(s)
let echof format = Printf.kprintf echo format

// print debug message to the output
let debug option cont = if options.HasFlag(option) then cont (Printf.kprintf (fun s -> echof "<%A> %s" option s))