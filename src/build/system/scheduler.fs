namespace BuildSystem

open System.Collections.Generic
open System.Diagnostics
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
    let task_by_output = Dictionary<string, TaskState>()
    let tasks_by_input = Dictionary<string, List<TaskState>>()
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
            // diff inputs
            yield! (diff lhs.Inputs rhs.Inputs fst
                (fun (path, s) -> sprintf "%s is a new dependency" path)
                (fun (path, s) -> sprintf "%s is no longer a dependency" path)
                (fun (path0, s0) (path1, s1) -> sprintf "%s changed" path0)
                (fun l r -> sprintf "dependency order changed (%A -> %A)" (l |> Array.map fst) (r |> Array.map fst)))
        }

    // is task current or does it need to be built?
    member private this.IsCurrent (task: Task, tsig: TaskSignature) =
        if not (task.Targets |> Array.forall (fun p -> p.Info.Exists)) then
            Output.debug Output.Options.DebugExplain (fun e -> e "Building %s: one of the targets does not exist" task.Uid)
            false
        else
            match db.TaskSignature task.Uid with
            | Some s ->
                if tsig = s then
                    true
                else
                    Output.debug Output.Options.DebugExplain (fun e ->
                        let diffs = this.Diff(s, tsig) |> Seq.toArray
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
                    false
            | None ->
                Output.debug Output.Options.DebugExplain (fun e -> e "Building %s: no previous build info" task.Uid)
                false

    // wait for task to complete, running it inline if necessary
    member private this.Wait (state: TaskState) =
        match state.Status with
        | TaskStatus.Created -> this.Run state
        | TaskStatus.Running -> failwithf "Dependency cycle found for task %s" state.Task.Uid
        | TaskStatus.Completed -> ()
        | _ -> failwithf "Unknown status %A for task %s" state.Status state.Task.Uid

    // run task
    member private this.Run (state: TaskState) =
        assert (state.Status = TaskStatus.Created)
        state.Status <- TaskStatus.Running

        let task = state.Task

        // wait for all inputs
        let inputs = task.Sources

        for input in inputs do
            match task_by_output.TryGetValue(input.Uid) with
            | true, dep -> this.Wait dep
            | _ -> ()

        // check task & build
        let tsig = TaskSignature(inputs |> Array.map (fun n -> n.Uid, db.ContentSignature n))

        if not (this.IsCurrent(task, tsig)) then
            let builder = task.Builder

            let descr = builder.Description task
            if descr <> null then Output.echo "%s" descr

            let result =
                try
                    for target in task.Targets do Directory.CreateDirectory(target.Info.DirectoryName) |> ignore
                    builder.Build task
                with
                | e -> failwithf "%s: error: %s" task.Uid e.Message

            db.TaskSignature task.Uid <- tsig

        state.Status <- TaskStatus.Completed

    // add task to processing
    member this.Add (task: Task) =
        let state = TaskState(task)

        // add task to input -> task map
        for input in task.Sources do
            match tasks_by_input.TryGetValue(input.Uid) with
            | true, list ->
                list.Add(state)
            | _ ->
                let list = new List<_>()
                list.Add(state)
                tasks_by_input.Add(input.Uid, list)

        // add task to output -> task map
        for output in task.Targets do
            task_by_output.Add(output.Uid, state)

        tasks.Add(state)

    // run all tasks
    member this.Run () =
        Output.echo "*** building %d targets... ***" tasks.Count
        let timer = Stopwatch.StartNew()
        for state in tasks do this.Wait state
        Output.echo "*** finished in %.2f sec ***" timer.Elapsed.TotalSeconds