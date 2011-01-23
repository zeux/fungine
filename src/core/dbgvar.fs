namespace Core

open System.Collections.Generic

module DbgVars =
    // variable storage
    type Cell(defaults) =
        let mutable value = defaults
        let event = Event<_>()

        // default value accessor
        member this.DefaultValue = defaults

        // value change notifications
        member this.ValueChanged = event.Publish

        // value accessor
        member this.Value
            with get () = value
            and set rhs =
                value <- rhs
                event.Trigger(box this)

    // all registered debug variables
    let private variables = Dictionary<string, Cell>()

    // register a debug variable
    let add description defaults =
        lock variables (fun () ->
            match variables.TryGetValue(description) with
            | true, cell ->
                // the cell already exists (the dbgvar was defined in two modules)
                assert (cell.DefaultValue = defaults)
                cell
            | _ ->
                // create a new cell
                let cell = Cell(box defaults)
                variables.Add(description, cell)
                cell)

    // get all debug variables
    let getVariables () =
        lock variables (fun () -> Seq.toArray variables |> Array.map (fun p -> p.Key, p.Value))

// debug variable handle
type DbgVar<'T>(defaults: 'T, description: string) =
    // auto-register variable in manager
    let value = DbgVars.add description defaults

    // value accessor
    member this.Value: 'T = unbox value.Value