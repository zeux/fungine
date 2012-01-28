namespace BuildSystem

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO

// build context
type Context(rootPath, buildPath, ?jobs) =
    static let mutable current: Context option = None

    // setup node root so that DB paths are stable
    do Node.Root <- rootPath

    let db = Database(buildPath + "/.builddb")
    let scheduler = TaskScheduler(db)
    let tasks = ConcurrentDictionary<string, Task>()
    let jobs = defaultArg jobs Environment.ProcessorCount

    // get build path
    member this.BuildPath = buildPath

    // get target
    member this.Target (source: Node) ext =
        if Path.IsPathRooted(source.Path) || source.Path.StartsWith("../") then failwithf "Out-of-source paths are not supported: %s" source.Path
        Node (Path.Combine(this.BuildPath, Path.ChangeExtension(source.Path, ext)))

    // add task
    member this.Task(task: Task) =
        // check task uniqueness
        if tasks.TryAdd(task.Uid, task) then
            scheduler.Add(task)
        else
            let t = tasks.[task.Uid]
            if task.Sources <> t.Sources || task.Builder <> t.Builder then failwithf "Duplicate task definitions found for task %s" task.Uid

    // add task with source/target list
    member this.Task(builder, sources: Node array, targets: Node array) =
        this.Task(Task(sources, targets, builder))

    // add task with single target
    member this.Task(builder, sources: Node array, target: Node) =
        this.Task(builder, sources, [| target |])

    // add task with single source/target
    member this.Task(builder, source: Node, target: Node) =
        this.Task(builder, [| source |], [| target |])

    // run all tasks
    member private this.RunAll () =
        // run all tasks
        let result =
            try
                // set current context for the duration of the build
                assert (current.IsNone)
                current <- Some this

                scheduler.Run(jobs)
            finally
                // reset current context
                current <- None

        // save database
        db.Flush()

        result

    // run all tasks
    member this.Run () =
        Output.echof "*** found %d targets, building... ***" tasks.Count

        let timer = Stopwatch.StartNew()
        
        match this.RunAll() with
        | _ -> Output.echof "*** built %d targets in %.2f sec ***" tasks.Count timer.Elapsed.TotalSeconds

    // run tasks for updated set of inputs
    member this.RunUpdated inputs =
        let count = scheduler.UpdateInputs inputs

        if count > 0 then this.Run()

    // current context accessor
    static member Current = current.Value
