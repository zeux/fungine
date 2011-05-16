namespace BuildSystem

open System.Collections.Generic

// build context
type Context(root_path, build_path) =
    // setup node root so that DB paths are stable
    do Node.Root <- root_path

    let db = Database(build_path + "/.builddb")
    let scheduler = TaskScheduler(db)
    let tasks = Dictionary<string, Task>()

    // get build path
    member this.BuildPath = build_path

    // add task
    member this.Task(builder, sources: Node array, targets: Node array) =
        let task = Task(sources, targets, builder, this)

        // check task uniqueness
        match tasks.TryGetValue(task.Uid) with
        | true, t ->
            if task.Sources <> t.Sources || task.Builder <> t.Builder then failwithf "Duplicate task definitions found for task %s" task.Uid
        | _ ->
            tasks.Add(task.Uid, task)
            scheduler.Add(task)

    // add task with single target
    member this.Task(builder, sources: Node array, target: Node) =
        this.Task(builder, sources, [| target |])

    // add task with single source/target
    member this.Task(builder, source: Node, target: Node) =
        this.Task(builder, [| source |], [| target |])

    // run all tasks
    member this.Run () =
        // run all tasks
        try
            scheduler.Run()
        with
        | e -> printfn "*** Error %s" e.Message

        // save database
        db.Flush()