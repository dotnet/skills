---
name: dotnet-fsi-interactive
description: "Run and test .NET code interactively without creating files. Explore .NET APIs, verify runtime behavior, experiment with code in a persistent REPL (Read-Eval-Print Loop) session via dotnet fsi. Also for data analysis and inspecting stateful objects."
---

# F# Interactive REPL

`dotnet fsi` — a REPL (Read-Eval-Print Loop) for .NET. **Single persistent process**: start once, send submissions repeatedly, state accumulates.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| .NET SDK | Yes | Any version with `dotnet fsi` |

## Sweet spots

- **API exploration** — reflect on types, list members, try overloads, `#help` for inline docs
- **Runtime behavior verification** — test type assignability, generic variance, serialization edge cases
- **Database inspection** — connect once, query without reconnecting
- **Iterative analysis of slow-to-retrieve data** — fetch once, slice and explore from different angles
- **NuGet experimentation** — `#r "nuget: Pkg"` to pull in any package on the fly

## When not to use

- Single script run (`dotnet fsi file.fsx`) suffices
- Task needs a full project with multiple files

## Workflow

### Step 1: Start session

```bash
dotnet fsi --nologo
```

Launch as async/background process, wait for `>`. `--nologo` suppresses banner noise. **Keep this process running for all subsequent steps.**

### Step 2: Send submissions, read results

A submission ends with `;;` and can contain **multiple expressions and let-bindings**. Batch related work into a single submission to minimize round-trips — every read echoes the full session, so fewer submissions = less token overhead.

```fsharp
open System;;
let minDate = DateTime.MinValue;;
printfn "MinValue: %A, DayOfWeek: %A" minDate minDate.DayOfWeek;;
DateTime(2025, 12, 31).DayOfYear;;
```

All results come back in one read. Send follow-up submissions to the **same session** — previous bindings are still alive.

### Step 3: End session

Terminate the process.

## Example: Exploring System.Console API

Single `dotnet fsi --nologo` session:

```fsharp
open System.Reflection;;
let methods = typeof<System.Console>.GetMethods(BindingFlags.Public ||| BindingFlags.Static);;
methods |> Array.map (fun m -> m.Name) |> Array.distinct |> Array.sort;;
```

Then drill into a specific method in the same session:

```fsharp
methods
|> Array.filter (fun m -> m.Name = "Beep")
|> Array.iter (fun m ->
    let ps = m.GetParameters() |> Array.map (fun p -> sprintf "%s: %s" p.Name (p.ParameterType.Name))
    printfn "%s(%s)" m.Name (String.concat ", " ps));;
```
Outputs:
```
Beep()
Beep(frequency: Int32, duration: Int32)
```

## Directives

| Directive | Purpose |
|---|---|
| `#r "nuget: Pkg";;` | Pull in a NuGet package on the fly |
| `#time "on";;` | Elapsed time, CPU, GC stats per eval |
| `#help List.map;;` | Inline docs for any function |
| `#r "path.dll";;` | Reference local assembly |
| `#load "file.fsx";;` | Load and run script |
| `#quit;;` | Exit session |

## Pitfalls

| Pitfall | Fix |
|---------|-----|
| Forgetting `;;` | FSI shows `-` prompt, waiting. Send `;;` alone to flush. |
| New process per submission | **Don't.** Reuse same session — state lost on restart. |
| One expression per submission | A single `;;`-terminated submission can contain multiple `let` bindings, `open` statements, and expressions. Batch them. |
