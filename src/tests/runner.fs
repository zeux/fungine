let timer = System.Diagnostics.Stopwatch.StartNew()
let passed, total = Core.Test.run ()

if passed = total then
    printfn "Success: %d tests passed in %.2f sec." passed timer.Elapsed.TotalSeconds
else
    printfn "FAILURE: %d out of %d tests failed." (total - passed) total