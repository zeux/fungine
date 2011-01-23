module Core.Test

// internal exception that's thrown by failed assertions
exception private AssertionException of string * int * string * string

// delegate type for test functions
type private TestDelegate = delegate of unit -> unit

// invoke the test method, maybe return test exception
let private runTest test =
    // create a typed delegate to avoid exception handling in Delegate.DynamicInvoke / MethodInfo.Invoke
    let d = System.Delegate.CreateDelegate(typedefof<TestDelegate>, test) :?> TestDelegate

    // avoid exception handling when a debugger is attached so that we can see the exception at the point of failure
    if System.Diagnostics.Debugger.IsAttached then
        d.Invoke()
        None
    else
        // convert the exception to maybe
        try
            d.Invoke()
            None
        with
        | e -> Some e

// run all tests in all loaded assemblies
let private runTests () =
    let assemblies = System.AppDomain.CurrentDomain.GetAssemblies()
    let types = assemblies |> Array.collect (fun a -> a.GetTypes()) 
    let suites = types |> Array.filter (fun t -> t.Name.EndsWith("Tests"))

    let total = ref 0
    let passed = ref 0

    for suite in suites do
        let methods = suite.GetMethods(System.Reflection.BindingFlags.Static ||| System.Reflection.BindingFlags.Public)
        let tests = methods |> Array.filter (fun m -> m.GetParameters().Length = 0)

        for test in tests do
            incr total

            match runTest test with
            | None -> incr passed
            | Some (AssertionException (file, line, name, msg)) ->
                printfn "%s: test %s failed:\n\t%s:%d: assertion failed in %s\n\t%s" suite.FullName test.Name file line name msg
            | Some e ->
                printfn "%s: test %s failed:\n\t%A" suite.FullName test.Name e

    !passed, !total

// run all tests with the assertion handler
let private runTestsWithAssertionHandler () =
    let fail msg =
        if System.Diagnostics.Debugger.IsAttached then
            System.Diagnostics.Debugger.Break()

        let trace = System.Diagnostics.StackTrace(fNeedFileInfo = true)
        let frame = trace.GetFrame(4)
        let line =
            try
                System.IO.File.ReadAllLines(frame.GetFileName()).[frame.GetFileLineNumber() - 1].Trim()
            with
            | _ -> "<unknown>"

        raise (AssertionException(frame.GetFileName(), frame.GetFileLineNumber(), frame.GetMethod().Name, line))

    let listener = { new System.Diagnostics.DefaultTraceListener() with member this.Fail(msg) = fail msg }

    System.Diagnostics.Debug.Listeners.Insert(0, listener)

    try
        runTests ()
    finally
        System.Diagnostics.Debug.Listeners.Remove(listener)

// run all tests
let run () =
    let passed, total = runTestsWithAssertionHandler ()

    if passed = total then
        printfn "Success: %d tests passed." passed
    else
        printfn "FAILURE: %d out of %d tests failed." (total - passed) total