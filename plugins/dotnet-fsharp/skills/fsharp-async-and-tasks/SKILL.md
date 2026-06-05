---
name: fsharp-async-and-tasks
description: "Write correct asynchronous F# with async {} and task {}, and bridge to .NET Task-based APIs. Use when writing async F#, choosing between async {} and task {}, fixing blocking calls (.Result/.Wait()) or deadlocks, propagating cancellation, running work in parallel, or interoperating with Task-returning .NET libraries. Covers async vs task semantics, Async.AwaitTask/Async.StartAsTask, Async.Parallel, and cancellation tokens. Do not use for shaping an async public API for C# consumers (use design-fsharp-for-dotnet-interop)."
license: MIT
---

# F# Async and Tasks

## Purpose

Write asynchronous F# that does not block threads or deadlock, and that interoperates cleanly
with the .NET `Task` world.

## When to Use

- Writing asynchronous F# (I/O, HTTP, database)
- Deciding between `async { }` and `task { }`
- Removing `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` blocking calls
- Running asynchronous work in parallel
- Calling `Task`-returning .NET APIs from F# and vice versa
- Threading cancellation tokens through async code

## When Not to Use

- Designing an async API surface for C# consumers - use `design-fsharp-for-dotnet-interop`

## async {} vs task {}

| | `async { }` | `task { }` |
|---|-------------|------------|
| Type | `Async<'T>` (a cold, composable description) | `Task<'T>` (hot, starts running) |
| Starts when | started explicitly (`Async.RunSynchronously`, `Async.Start`, `Async.StartAsTask`) | immediately on creation |
| Cancellation | implicit ambient `CancellationToken` | pass the token explicitly |
| Best for | composable F# pipelines, parallelism, ret/ cancellation as values | direct interop with `Task` .NET APIs, simplest call-through |

Rule of thumb: prefer `task { }` when the surrounding code is `Task`-centric or interop-heavy;
prefer `async { }` when you compose, retry, or parallelize many operations and want them as
first-class values.

## Never block on async

```fsharp
// WRONG - blocks the thread, can deadlock
let content = httpClient.GetStringAsync(url).Result
```

```fsharp
// task: await it
let fetch url =
    task {
        let! content = httpClient.GetStringAsync url
        return content
    }
```

```fsharp
// async: await a Task with Async.AwaitTask
let fetch url =
    async {
        let! content = httpClient.GetStringAsync url |> Async.AwaitTask
        return content
    }
```

## Bridging async and Task

```fsharp
// Task -> Async
let a = someTask |> Async.AwaitTask

// Async -> Task (e.g. to hand to a Task-based API)
let t = someAsync |> Async.StartAsTask

// run an Async to a value at a boundary (top-level only)
let value = someAsync |> Async.RunSynchronously
```

## Parallelism

```fsharp
let fetchAll urls =
    urls
    |> List.map fetchAsync          // url list -> Async<string> list
    |> Async.Parallel               // -> Async<string[]>
```

For `Task`, use `System.Threading.Tasks.Task.WhenAll`.

## Cancellation

`async { }` picks up the ambient cancellation token automatically:

```fsharp
let work = async { ... }
Async.RunSynchronously(work, cancellationToken = token)
```

For `task { }`, accept a `CancellationToken` parameter and pass it to the calls you make.

## Workflow

1. Pick `async { }` or `task { }` based on interop vs composition.
2. Replace every blocking `.Result`/`.Wait()` with `let!` (awaiting via `Async.AwaitTask` in
   `async`).
3. For many independent operations, use `Async.Parallel` / `Task.WhenAll`.
4. Thread cancellation: ambient for `async`, explicit token for `task`.
5. Only convert to a synchronous value (`Async.RunSynchronously`) at the outermost boundary.
6. Verify with `dotnet fsi`.

## Validation

- [ ] No `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` inside async code
- [ ] `async`/`task` choice matches the surrounding code (interop vs composition)
- [ ] Independent operations run with `Async.Parallel` / `Task.WhenAll`, not sequentially
- [ ] Cancellation is propagated (ambient for async, explicit token for task)
- [ ] Code compiles and runs (`dotnet fsi`)

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| `.Result` to "get the value" | `let!` inside `async`/`task`; never block |
| Expecting `async { }` to start on its own | `Async<'T>` is cold; start it (`StartAsTask`/`RunSynchronously`) |
| `Async.RunSynchronously` deep in a call chain | Only at the top-level boundary |
| Sequential `let!`s for independent work | Use `Async.Parallel` / `Task.WhenAll` |
| Dropping the cancellation token in `task { }` | Accept and forward a `CancellationToken` |

## More info

- Async programming: https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/async-expressions
- Task expressions: https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/task-expressions
