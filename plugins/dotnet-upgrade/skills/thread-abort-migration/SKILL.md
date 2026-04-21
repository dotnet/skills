---
name: thread-abort-migration
description: >
  Guides migration of .NET Framework Thread.Abort usage to cooperative cancellation
  in modern .NET.
  USE FOR: modernizing code that calls Thread.Abort, catching ThreadAbortException,
  replacing Thread.ResetAbort, replacing Thread.Interrupt for thread termination,
  resolving PlatformNotSupportedException or SYSLIB0006 after retargeting to .NET 6+,
  migrating ASP.NET Response.End or Response.Redirect(url, true) which internally
  call Thread.Abort.
  DO NOT USE FOR: code that only uses Thread.Join, Thread.Sleep, or Thread.Start
  without any abort, interrupt, or ThreadAbortException usage — these APIs work
  identically in modern .NET and need no migration. Also not for projects staying
  on .NET Framework, or Thread.Abort usage inside third-party libraries you do not
  control.
---

# Thread.Abort Migration

This skill helps an agent migrate .NET Framework code that uses `Thread.Abort` to the cooperative cancellation model required by modern .NET (6+). `Thread.Abort` throws `PlatformNotSupportedException` in modern .NET — there is no way to forcibly terminate a managed thread. The skill identifies the usage pattern first, then applies the correct replacement strategy.

## When to Use

- Migrating a .NET Framework project to .NET 6+ that calls `Thread.Abort`
- Replacing `ThreadAbortException` catch blocks that use control flow or cleanup logic
- Removing `Thread.ResetAbort` calls that cancel pending aborts
- Replacing `Thread.Interrupt` for waking blocked threads
- Migrating ASP.NET code that uses `Response.End` or `Response.Redirect(url, true)`, which internally call `Thread.Abort`
- Resolving `PlatformNotSupportedException` or `SYSLIB0006` warnings after a target framework change

## When Not to Use

- **The code only uses `Thread.Join`, `Thread.Sleep`, or `Thread.Start` without any abort, interrupt, or `ThreadAbortException` catch blocks.** These APIs work identically in modern .NET — no migration is needed. Stop here and tell the user no migration is required. If you suggest modernization (e.g., `Task.Run`, `Parallel.ForEach`), you **must** explicitly state these are optional improvements unrelated to Thread.Abort migration, and the existing code will compile and run correctly as-is on the target framework.
- The project will remain on .NET Framework indefinitely
- The Thread.Abort usage is inside a third-party library you do not control

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Source project or solution | Yes | The .NET Framework project containing Thread.Abort usage |
| Target framework | Yes | The modern .NET version to target (e.g., `net8.0`) |
| Thread.Abort usage locations | Recommended | Files or classes that reference `Thread.Abort`, `ThreadAbortException`, `Thread.ResetAbort`, or `Thread.Interrupt` |

## Workflow

> **Commit strategy:** Commit after each pattern replacement so the migration is reviewable and bisectable. Group related call sites (e.g., all cancellable work loops) into one commit.

### Step 1: Inventory all thread termination usage

Search the codebase for all thread-termination-related APIs:

- `Thread.Abort` and `thread.Abort()` (instance calls)
- `ThreadAbortException` in catch blocks
- `Thread.ResetAbort`
- `Thread.Interrupt`
- `Response.End()` (calls Thread.Abort internally in ASP.NET Framework)
- `Response.Redirect(url, true)` (the `true` parameter triggers Thread.Abort)
- `SYSLIB0006` pragma suppressions

Record each usage location and classify the intent behind the abort.

### Step 2: Classify each usage pattern

Categorize every usage into one of the following patterns:

| Pattern | Description | Modern replacement |
|---------|-------------|--------------------|
| **Cancellable work loop** | Thread running a loop that should stop on demand | `CancellationToken` checked in the loop |
| **Timeout enforcement** | Aborting a thread that exceeds a time limit | `CancellationTokenSource.CancelAfter` or `Task.WhenAny` with a delay |
| **Blocking call interruption** | Thread blocked on `Sleep`, `WaitOne`, or `Join` that needs to wake up | `WaitHandle.WaitAny` with `CancellationToken.WaitHandle`, or async alternatives |
| **ASP.NET request termination** | `Response.End` or `Response.Redirect(url, true)` | Return from the action method; use `HttpContext.RequestAborted` |
| **ThreadAbortException as control flow** | Catch blocks that inspect `ThreadAbortException` to decide cleanup actions | Catch `OperationCanceledException` instead, with explicit cleanup |
| **Thread.ResetAbort to continue execution** | Catching the abort and calling `ResetAbort` to keep the thread alive | Check `CancellationToken.IsCancellationRequested` and decide whether to continue |
| **Uncooperative code termination** | Killing a thread running code that cannot be modified to check for cancellation | Move the work to a separate process and use `Process.Kill` |

**Critical:** The fundamental paradigm shift is from preemptive cancellation (the runtime forcibly injects an exception) to cooperative cancellation (the code must voluntarily check for and respond to cancellation requests). Every call site must be evaluated for whether the target code can be modified to cooperate.

### Step 3: Apply the replacement for each pattern

- **Cancellable work loop**: Add a `CancellationToken` parameter. Replace the loop condition or add `token.ThrowIfCancellationRequested()` at safe checkpoints. The caller creates a `CancellationTokenSource` and calls `Cancel()` instead of `Thread.Abort()`.
- **Timeout enforcement**: Use `new CancellationTokenSource(TimeSpan.FromSeconds(n))` or `cts.CancelAfter(timeout)`. Pass the token to the work. For task-based code, use `Task.WhenAny(workTask, Task.Delay(timeout, cts.Token))` and cancel the source if the delay wins; cancelling also disposes the delay's internal timer.
- **Blocking call interruption**: Replace `Thread.Sleep(ms)` with `Task.Delay(ms, token)` or `token.WaitHandle.WaitOne(ms)`. Replace `ManualResetEvent.WaitOne()` with `WaitHandle.WaitAny(new[] { event, token.WaitHandle })`.
- **ASP.NET request termination**: Remove `Response.End()` entirely — just return from the method. Replace `Response.Redirect(url, true)` with `Response.Redirect(url)` (without the `true` endResponse parameter) or return a redirect result. In ASP.NET Core, use `HttpContext.RequestAborted` as the cancellation token for long-running request work.
- **ThreadAbortException as control flow**: Replace `catch (ThreadAbortException)` with `catch (OperationCanceledException)`. Move cleanup logic to `finally` blocks or `CancellationToken.Register` callbacks. Do not catch `OperationCanceledException` and swallow it — let it propagate unless you have a specific recovery action.
- **Thread.ResetAbort to continue execution**: Break up "abortable" units of work so that cancellation in a processing loop can continue to the next unit instead of relying on `ResetAbort` to prevent tearing down the thread. Check `token.IsCancellationRequested` after each unit and decide whether to continue. Create a new `CancellationTokenSource` (optionally linked to a parent token) for each new unit of work rather than trying to reset an existing one.
- **Uncooperative code termination**: If the code cannot be modified to accept a `CancellationToken` (e.g., third-party library, native call), move the work to a child process. The host process communicates via stdin/stdout or IPC and calls `Process.Kill` if a timeout expires.

### Step 4: Clean up removed APIs

After migrating all patterns, remove or replace any remaining references:

| Removed API | Replacement |
|-------------|-------------|
| `Thread.Abort()` | `CancellationTokenSource.Cancel()` |
| `ThreadAbortException` catch blocks | `OperationCanceledException` catch blocks |
| `Thread.ResetAbort()` | Check `token.IsCancellationRequested` and decide whether to continue |
| `Thread.Interrupt()` | Signal via `CancellationToken` or set a `ManualResetEventSlim` (also obsolete: `SYSLIB0046` in .NET 9) |
| `Response.End()` | Remove the call; return from the method |
| `Response.Redirect(url, true)` | `Response.Redirect(url)` without endResponse, or return a redirect result |
| `#pragma warning disable SYSLIB0006` | Remove after replacing the Thread.Abort call |

### Step 5: Verify the migration

1. Build the project targeting the new framework. Confirm zero `SYSLIB0006` warnings and no `Thread.Abort`-related compile errors.
2. Search the codebase for any remaining references to `Thread.Abort`, `ThreadAbortException`, `Thread.ResetAbort`, or `Thread.Interrupt`.
3. Run existing tests. If tests relied on `Thread.Abort` for cleanup or timeout, update them to use `CancellationToken`.
4. For timeout scenarios, verify that work actually stops within a reasonable time after cancellation is requested.
5. For blocking call scenarios, verify that blocked threads wake up promptly when the token is cancelled.

## Validation

- [ ] No references to `Thread.Abort` remain in the migrated code
- [ ] No `ThreadAbortException` catch blocks remain
- [ ] No `Thread.ResetAbort` calls remain
- [ ] No `SYSLIB0006` pragma suppressions remain
- [ ] Project builds cleanly against the target framework with no thread-abort-related warnings
- [ ] All cancellable work accepts a `CancellationToken` parameter
- [ ] Timeout scenarios use `CancellationTokenSource.CancelAfter` or equivalent
- [ ] Blocking calls use `WaitHandle.WaitAny` with `token.WaitHandle` or async alternatives
- [ ] Existing tests pass or have been updated for cooperative cancellation

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Adding `CancellationToken` parameter but never checking it in long-running code | Insert `token.ThrowIfCancellationRequested()` at regular checkpoints in loops and between expensive operations. Cancellation only works if the code cooperates. |
| Not passing the token through the full call chain | Every async or long-running method in the chain must accept and forward the `CancellationToken`. If one method in the chain ignores it, cancellation stalls at that point. |
| Expecting `CancellationToken` to interrupt blocking synchronous calls like `Thread.Sleep` or `socket.Receive` | These calls do not check the token. Replace `Thread.Sleep(ms)` with `token.WaitHandle.WaitOne(ms)`. Replace synchronous I/O with async overloads that accept a `CancellationToken`. |
| Catching `OperationCanceledException` and swallowing it | Let `OperationCanceledException` propagate to the caller. Only catch it at the top-level orchestration point where you decide what to do after cancellation (log, clean up, return a result). |
| Not disposing `CancellationTokenSource` | `CancellationTokenSource` is `IDisposable`. Wrap it in a `using` statement or dispose it in a `finally` block. Leaking it causes timer and callback leaks. |
| Assuming cancellation is immediate | Cooperative cancellation only takes effect at the next checkpoint. If work items are large or the code has long gaps between checks, cancellation may be delayed. Design checkpoint frequency based on acceptable latency. |
| Using `Thread.Interrupt` as a substitute for `Thread.Abort` | `Thread.Interrupt` is also not recommended in modern .NET. It only works on threads in `WaitSleepJoin` state and throws `ThreadInterruptedException`, which is a different exception type. Replace with `CancellationToken` signaling. |
| Removing `ThreadAbortException` catch blocks without migrating the cleanup logic | `ThreadAbortException` catch blocks often contained critical cleanup (releasing locks, rolling back transactions). Move this logic to `finally` blocks or `CancellationToken.Register` callbacks before removing the catch. |

## More Info

- [Thread.Abort breaking change in .NET 6+](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/thread-abort) — why `Thread.Abort` throws `PlatformNotSupportedException`
- [Cancellation in managed threads](https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads) — the cooperative cancellation model with `CancellationToken`
- [CancellationTokenSource class](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtokensource) — API reference for creating and managing cancellation tokens
- [SYSLIB0006 warning](https://learn.microsoft.com/dotnet/fundamentals/syslib-diagnostics/syslib0006) — `Thread.Abort` is obsolete
- [SYSLIB0046 warning](https://learn.microsoft.com/dotnet/fundamentals/syslib-diagnostics/syslib0046) — `Thread.Interrupt` is obsolete (added in .NET 9)
- [`Task.WaitAsync(CancellationToken)`](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.waitasync) — cancellable waiting for task-based code (.NET 6+)
