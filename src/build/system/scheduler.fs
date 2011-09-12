namespace BuildSystem

open System.Collections.Generic
open System.IO

// current task status
type private TaskStatus =
    | Created = 0
    | Running = 1
    | Completed = 2

// current task state
type private TaskState(task: Task) =
    let mutable status = TaskStatus.Created

    // task
    member this.Task = task

    // task status
    member this.Status
        with get () = status
        and set value = status <- value

// synchronous task scheduler
type private TaskScheduler(db: Database) =
    let taskByOutput = Dictionary<string, TaskState>()
    let tasksByInput = Dictionary<string, List<TaskState>>()
    let tasks = List<TaskState>()

    // list of differences between two signatures
    member private this.Diff (lhs: TaskSignature, rhs: TaskSignature) =
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
    member private this.ContentSignature (node: Node) =
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
                let tsigi = TaskSignature(tsig.Inputs, s.Implicits |> Array.map (fun (uid, _) -> uid, this.ContentSignature(Node uid)), tsig.Version, None)

                if tsigi.Inputs = s.Inputs && tsigi.Implicits = s.Implicits && tsigi.Version = s.Version then
                    Some s
                else
                    Output.debug Output.Options.DebugExplain (fun e ->
                        let diffs = this.Diff(s, tsigi) |> Seq.toArray
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

    // wait for task to complete, running it inline if necessary
    member private this.Wait (state: TaskState) =
        match state.Status with
        | TaskStatus.Created -> this.Run state
        | TaskStatus.Running -> failwithf "Dependency cycle found for task %s" state.Task.Uid
        | TaskStatus.Completed -> ()
        | _ -> failwithf "Unknown status %A for task %s" state.Status state.Task.Uid

    // wait for node to be complete
    member private this.Wait (node: Node) =
        match taskByOutput.TryGetValue(node.Uid) with
        | true, dep -> this.Wait dep
        | _ -> ()

    // add state dependency
    member private this.AddDependency (state, node: Node) =
        match tasksByInput.TryGetValue(node.Uid) with
        | true, list ->
            list.Add(state)
        | _ ->
            let list = new List<_>()
            list.Add(state)
            tasksByInput.Add(node.Uid, list)

    // run task with implicit dependency processing
    member private this.RunTaskImplicitDeps (task: Task) =
        let deps = Dictionary<string, string * Signature>()
        let oldh = task.Implicit

        // this is... ugly.
        // a better solution would be to have a separate mutable taskstate,
        // but it's not clear yet whether other parts of task should be mutable
        try
            task.Implicit <- fun node -> this.Wait(node); deps.[node.Uid] <- (node.Uid, db.ContentSignature node)
            let result = task.Builder.Build task
            result, Seq.toArray deps.Values
        finally
            task.Implicit <- oldh

    // run task
    member private this.Run (state: TaskState) =
        assert (state.Status = TaskStatus.Created)
        state.Status <- TaskStatus.Running

        let task = state.Task
        let builder = task.Builder

        try
            // compute current signature
            let tsig = TaskSignature(task.Sources |> Array.map (fun n -> n.Uid, this.ContentSignature n), [||], builder.Version task, None)

            // build task if necessary
            let (result, implicits) =
                match this.UpToDate(task, tsig) with
                | Some s -> s.Result, s.Implicits
                | None ->
                    // output task description
                    let descr = builder.Description task
                    if descr <> null then Output.echo descr

                    // make sure all targets can be created
                    for target in task.Targets do Directory.CreateDirectory(target.Info.DirectoryName) |> ignore

                    // build task
                    let result, implicits = this.RunTaskImplicitDeps task

                    // store signature with updated result
                    db.TaskSignature task.Uid <- TaskSignature(tsig.Inputs, implicits, tsig.Version, result)

                    result, implicits

            // run post-build step if necessary
            match result with
            | Some result -> builder.PostBuild(task, result)
            | None -> ()

            // add implicit dependencies to input -> task map
            for (input, _) in implicits do
                this.AddDependency(state, Node input)
        with
        | e -> failwithf "%s: failed to build target:\n%s" task.Uid e.Message

        state.Status <- TaskStatus.Completed

    // add task to processing
    member this.Add (task: Task) =
        let state = TaskState(task)

        // add task to input -> task map
        for input in task.Sources do
            this.AddDependency(state, input)

        // add task to output -> task map
        for output in task.Targets do
            taskByOutput.Add(output.Uid, state)

        tasks.Add(state)

    // run all tasks
    member this.Run () =
        // use manual loop instead of foreach because we can add tasks during Run ()
        let rec loop i =
            if i < tasks.Count then
                this.Wait tasks.[i]
                loop (i + 1)

        loop 0

    // process file updates so that the next run will build the dependent tasks
    member this.UpdateInputs inputs =
        let rec update (input: Node) =
            match tasksByInput.TryGetValue(input.Uid) with
            | true, list ->
                // update all tasks that depend on input
                list |> Seq.sumBy (fun task ->
                    if task.Status = TaskStatus.Created then 0
                    else
                        // mark task as not ready & recursively process outputs
                        task.Status <- TaskStatus.Created
                        1 + (task.Task.Targets |> Array.sumBy update))
            | _ -> 0

        inputs |> Array.sumBy update