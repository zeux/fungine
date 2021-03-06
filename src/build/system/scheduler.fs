namespace BuildSystem

open System
open System.Collections.Generic
open System.Collections.Concurrent
open System.IO
open System.Threading

// current task state
type private TaskState =
    { task: Task
      mutable runner: Tasks.Task }

// Threading.TaskScheduler implementation for specified concurrency level; guarantees that inline task execution succeeds
// Tasks are only added before Start() or from other tasks, so workers can determine when the last task is done and exit
// Effectively, waiting for workers to exit is equivalent to waiting for the dynamically spawned task tree to be complete
type private ConcurrentTaskScheduler(jobs) as this =
    inherit Tasks.TaskScheduler()

    let tasks = new BlockingCollection<_>(ConcurrentQueue())
    let workers = Array.init jobs (fun _ -> new Tasks.Task(this.Worker, Tasks.TaskCreationOptions.LongRunning))

    let mutable taskCounter = 0

    member private this.TryExecuteTask(task) =
        if base.TryExecuteTask(task) then
            let res = Interlocked.Decrement(&taskCounter)
            assert (res >= 0)

            // We processed last task, so it's impossible for any more tasks to be added
            // Mark the task queue as complete so that all workers can fail to take one more task
            if res = 0 then tasks.CompleteAdding()
            true
        else
            false

    member this.RunAll() =
        if taskCounter > 0 then
            for w in workers do w.Start()
            Tasks.Task.WaitAll(workers)

    member this.Worker () =
        let mutable task = Unchecked.defaultof<_>
        while tasks.TryTake(&task, Timeout.Infinite) do
            this.TryExecuteTask(task) |> ignore

    override this.QueueTask(task) =
        Interlocked.Increment(&taskCounter) |> ignore
        tasks.Add(task)

    override this.TryExecuteTaskInline(task, taskWasPreviouslyQueued) = this.TryExecuteTask(task)
    override this.GetScheduledTasks() = tasks.ToArray() |> Seq.ofArray
    override this.MaximumConcurrencyLevel = jobs

// synchronous task scheduler
type private TaskScheduler(db: Database) =
    let taskByOutput = ConcurrentDictionary<string, TaskState>()
    let tasksByInput = ConcurrentDictionary<string, List<TaskState>>()
    let tasks = Dictionary<string, TaskState>()
    let mutable scheduler = None

    // list of differences between two signatures
    static member private Diff (lhs: TaskSignature, rhs: TaskSignature) =
        // diff two arrays
        let diff left right sortkey add remove change order =
            seq {
                if Array.sortBy sortkey left = Array.sortBy sortkey right then
                    if left <> right then yield (order left right)
                else
                    let ld = left |> Array.map (fun e -> sortkey e, e) |> dict
                    let rd = right |> Array.map (fun e -> sortkey e, e) |> dict

                    // process left-side removals & changes
                    for l in ld.Values do
                        match rd.TryGetValue(sortkey l) with
                        | true, r -> if l <> r then yield (change l r)
                        | _ -> yield (remove l)

                    // process right-side adds
                    for r in rd.Values do
                        if not (ld.ContainsKey(sortkey r)) then
                            yield (add r)
            }

        seq {
            let diffdep l r =
                diff l r fst
                    (fun (path, s) -> sprintf "%s is a new dependency" path)
                    (fun (path, s) -> sprintf "%s is no longer a dependency" path)
                    (fun (path0, s0) (path1, s1) -> sprintf "%s changed" path0)
                    (fun l r -> sprintf "dependency order changed (%A -> %A)" (l |> Array.map fst) (r |> Array.map fst))

            // diff dependencies
            yield! (diffdep lhs.Inputs rhs.Inputs)
            yield! (diffdep lhs.Implicits rhs.Implicits)

            // diff versions
            if lhs.Version <> rhs.Version then yield (sprintf "version changed (%A -> %A)" lhs.Version rhs.Version)
        }

    // get up-to-date content signature for node
    member private this.CurrentContentSignature (node: Node) =
        this.Wait(node)
        db.ContentSignature(node)

    // is task current or does it need to be built?
    member private this.UpToDate (task: Task, tsig: TaskSignature) =
        if not (task.Targets |> Array.forall (fun p -> p.Info.Exists)) then
            Output.debug Output.Options.DebugExplain (fun e -> e "Building %s: one of the targets does not exist" task.Uid)
            None
        else
            match db.TaskSignature task.Uid with
            | Some s ->
                // get new signatures for old implicit deps
                let tsigi = TaskSignature(tsig.Inputs, s.Implicits |> Array.map (fun (uid, _) -> uid, this.CurrentContentSignature(Node uid)), tsig.Version, None)

                if tsigi.Inputs = s.Inputs && tsigi.Implicits = s.Implicits && tsigi.Version = s.Version then
                    Some s
                else
                    Output.debug Output.Options.DebugExplain (fun e ->
                        let diffs = TaskScheduler.Diff(s, tsigi) |> Seq.toArray
                        let reason =
                            match diffs with
                            | [||] -> "for unknown reason"
                            | [| r |] -> "because " + r
                            | _ ->
                                diffs
                                |> Array.map (sprintf "\t* %s")
                                |> String.concat "\n"
                                |> sprintf "because:\n%s"

                        e "Building %s %s" task.Uid reason)
                    None
            | None ->
                Output.debug Output.Options.DebugExplain (fun e -> e "Building %s: no previous build info" task.Uid)
                None

    // wait for task to complete
    member private this.Wait (state: TaskState) =
        let r = state.runner
        if r <> null then r.Wait()

    // wait for node to be complete
    member private this.Wait (node: Node) =
        match taskByOutput.TryGetValue(node.Uid) with
        | true, dep -> this.Wait dep
        | _ -> ()

    // add state dependency
    member private this.AddDependency (state, node: Node) =
        let list = tasksByInput.GetOrAdd(node.Uid, fun _ -> new List<_>())
        lock list (fun _ -> list.Add(state))

    // run task with implicit dependency processing
    member private this.RunTaskImplicitDeps (task: Task) =
        let deps = Dictionary<string, string * Signature>()
        let oldh = task.Implicit

        // this is... ugly.
        // a better solution would be to have a separate mutable taskstate,
        // but it's not clear yet whether other parts of task should be mutable
        try
            task.Implicit <- fun node -> deps.[node.Uid] <- (node.Uid, this.CurrentContentSignature node)
            let result = task.Builder.Build task
            result, Seq.toArray deps.Values
        finally
            task.Implicit <- oldh

    // run task
    member private this.RunTask (task: Task) =
        // output task description
        let descr = task.Builder.Description task
        if descr <> null then Output.echo descr

        // make sure all targets can be created
        for target in task.Targets do Directory.CreateDirectory(target.Info.DirectoryName) |> ignore

        // build task
        this.RunTaskImplicitDeps task

    // run task failed, output the exception and terminate the build as soon as possible
    member private this.RunTaskError (task: Task, error: exn) =
        // make sure we'll build this task again next time
        db.UpdateTaskSignature task.Uid None

        // print the error (we swallow the exception for now)
        Output.echof "%s: failed to build target:\n%s" task.Uid error.Message

    // run task
    member private this.Run (state: TaskState) =
        let task = state.task

        try
            // compute current signature
            let tsig = TaskSignature(task.Sources |> Array.map (fun n -> n.Uid, this.CurrentContentSignature n), [||], task.Builder.Version task, None)

            // build task if necessary
            let (result, implicits) =
                match this.UpToDate(task, tsig) with
                | Some s -> s.Result, s.Implicits
                | None ->
                    // build task
                    let result, implicits = this.RunTask task

                    // store signature with updated result
                    db.UpdateTaskSignature task.Uid <| Some (TaskSignature(tsig.Inputs, implicits, tsig.Version, result))

                    result, implicits

            // run post-build step if necessary
            match result with
            | Some result -> task.Builder.PostBuild(task, result)
            | None -> ()

            // add implicit dependencies to input -> task map
            for (input, _) in implicits do this.AddDependency(state, Node input)
        with e ->
            this.RunTaskError(task, e)

    // create task runner
    member private this.CreateTaskRunner (state: TaskState) =
        new Tasks.Task(fun _ -> this.Run(state))

    // add task to processing
    member this.Add (task: Task) =
        lock tasks (fun _ ->
            match tasks.TryGetValue(task.Uid) with
            | true, state ->
                let t = state.task
                if t.Sources <> task.Sources || t.Builder <> task.Builder then failwithf "Duplicate task definitions found for task %s" task.Uid
            | _ ->
                let state = { new TaskState with task = task and runner = null }
                state.runner <- this.CreateTaskRunner(state)

                // add task to input -> task map
                for input in task.Sources do
                    this.AddDependency(state, input)

                // add task to output -> task map
                for output in task.Targets do
                    match taskByOutput.TryAdd(output.Uid, state) with
                    | true -> ()
                    | _ -> failwithf "Duplicate tasks found for output %s" output.Uid

                tasks.Add(task.Uid, state)

                // if we're building tasks, start this one immediately
                match scheduler with
                | Some s -> state.runner.Start(s)
                | _ -> ())

    // get task count
    member this.TaskCount = tasks.Count

    // run all tasks
    member this.Run jobs =
        let sch = ConcurrentTaskScheduler(jobs)

        try
            assert (scheduler.IsNone)
            scheduler <- Some sch

            for t in lock tasks (fun _ -> tasks.Values |> Seq.toArray) do
                if t.runner <> null && t.runner.Status = Tasks.TaskStatus.Created then
                    t.runner.Start(sch)
    
            sch.RunAll()
        finally
            for t in tasks.Values do t.runner <- null
            scheduler <- None

    // process file updates so that the next run will build the dependent tasks
    member this.UpdateInputs inputs =
        let rec update (input: Node) =
            match tasksByInput.TryGetValue(input.Uid) with
            | true, list ->
                // update all tasks that depend on input
                list |> Seq.sumBy (fun state ->
                    if state.runner <> null then 0
                    else
                        // mark task as not ready & recursively process outputs
                        state.runner <- this.CreateTaskRunner state
                        1 + (state.task.Targets |> Array.sumBy update))
            | _ -> 0

        inputs |> Array.sumBy update
