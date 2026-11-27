---
name: process-api-net11
description: >
  Launch and supervise executable child processes on .NET 11 or later with System.Diagnostics.Process or SafeProcessHandle.
  DO NOT USE FOR: opening URLs in the default browser or documents in their associated application.
  Those are operating-system shell-association requests, not executable child-process management, even on .NET 11.
  DO NOT USE FOR: projects that must remain on .NET 10 or earlier.
  USE FOR: launching executables; preventing redirected stdout/stderr pipe deadlocks; capturing text, bytes, or interleaved lines;
  waiting for exit status; cancellation and timeouts; redirecting standard handles to files, pipes, or the null device;
  restricting handle inheritance; custom native process creation; fire-and-forget starts; parent-exit termination; terminal detachment;
  SafeProcessHandle and trimmer-friendly process management for NativeAOT applications;
  reviewing, modernizing, or fixing bugs in existing process code.
license: MIT
---

# Starting and Managing Processes in .NET 11

Choose the appropriate .NET 11 Process APIs for launching executables, capturing or forwarding output, waiting for termination, and controlling handle inheritance and child lifetime. Recognize common deadlock patterns and preserve the intended output and lifetime behavior.

## When to Use

For applications targeting .NET 11 or later:

- Starting an executable and waiting for its exit status, with captured, inherited, or discarded output.
- Reading stdout and stderr as text, bytes, or interleaved lines while avoiding pipe-buffer deadlocks.
- Redirecting standard handles to files, other processes, or the null device.
- Controlling handle inheritance, including custom native process creation.
- Managing cancellation, timeouts, parent-exit termination, or terminal detachment.

## When Not to Use

- The project must remain on .NET 10 or earlier; do not introduce APIs unavailable on its target framework.
- Opening a URL or document through the operating system's shell rather than launching an executable.

## Target Framework

Inspect the project's actual `TargetFramework` or `TargetFrameworks` before introducing these APIs; they require .NET 11 or later. Do not upgrade the target framework implicitly. For multi-targeted projects that include older frameworks, preserve those targets and their existing behavior; use conditional compilation or framework-specific implementations to keep .NET 11 APIs out of older-target builds.

```xml
<TargetFramework>net11.0</TargetFramework>
```

## Key Rules

- When redirecting standard output and standard error, drain both concurrently while the process runs. Never wait for exit before reading, and never read them sequentially; a full pipe blocks the child. See [output capture](references/output-capture.md).
- Never redirect output to pipes merely to suppress it; use `silent: true` or null handles instead. See [running without capture](references/running-without-capture.md).
- Never capture output merely to write it unchanged to the parent's console; use `Run[Async]` without redirection so the child writes to the inherited streams directly.
- Output EOF is not process termination, and termination is not EOF. Status waits do not drain pipes, and a descendant holding a pipe's write end prevents EOF.
- Canceling a wait or disposing a `Process` or `SafeProcessHandle` does not terminate the child. Choose deliberately between wait-only and kill-and-wait behavior.
- Always restrict inheritance with `ProcessStartInfo.InheritedHandles` on every platform: use `[]` when the child needs no additional handles, or list only the handles it requires. Standard handles are still inherited and can reach grandchildren. See [handle inheritance](references/handle-inheritance.md) for details.
- Full capture buffers all output in memory. For untrusted or potentially unlimited output, use fixed-size reads with one shared retention budget across stdout and stderr, and keep draining after the budget is reached.
- Prefer argument collections over concatenating, interpolating, or joining arguments into `ProcessStartInfo.Arguments`.
- Do not manually copy the parent's environment variables: `ProcessStartInfo.Environment` and `EnvironmentVariables` already initialize from them automatically. Apply only the child-specific changes.
- Keep `ProcessStartInfo.UseShellExecute` at its default value of `false`. Leave the property unset rather than explicitly assigning `false`: when the application never sets it, the AOT compiler can eliminate the unused shell-execution code.
- When `ProcessStartInfo.UseShellExecute` is `false` (or unset), `Process.Start` can never return `null` (it throws on error). Use `Process.Start(startInfo)!`; no redundant null check or `?? throw` is needed.
- When `ProcessStartInfo.UseShellExecute` is `true`, `Process.Start` can return `null` even after successfully opening a URL or document. That `null` should not be treated as a failure.
- Never use `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()` to synchronously block on an asynchronous process operation: sync-over-async can deadlock and blocks the calling thread. Do not call synchronous process-wait methods from asynchronous code either.

## Workflow

| Task | Steps |
|------|-------|
| Writing new process code, such as running a tool and capturing its output | 1, 2, 4, 5 |
| Modifying, modernizing, or reviewing existing process code, including searching it for bugs | 1-5 |

1. Inspect the target frameworks and the existing synchronous or asynchronous calling model, as described in [Target Framework](#target-framework) and [Choosing Synchronous or Asynchronous APIs](#choosing-synchronous-or-asynchronous-apis).
2. Identify the behavioral contract: where output goes, what completion means, what happens on timeout or cancellation, and who owns the child's lifetime.
3. For existing code only: read [anti-patterns](references/anti-patterns.md), then search all process-management code for every listed pattern, not only the call sites you already found. Search for `Process.Start`, `new Process`, `ProcessStartInfo`, `RedirectStandard`, `WaitForExit`, `ReadToEnd`, `BeginOutputReadLine`, `DataReceived`, `Exited`, `HasExited`, `ExitCode`, `Kill(`, and `Environment.SetEnvironmentVariable`.
4. Choose the API from [Choosing an API](#choosing-an-api) and read the matching references from the [Reference Guide](#reference-guide).
5. Make the smallest change that preserves existing output, logging, environment, cleanup, and error behavior, then validate every affected target framework.

## Choosing Synchronous or Asynchronous APIs

When modernizing existing code, preserve its synchronous or asynchronous calling model unless a concrete requirement justifies changing it. Do not propagate `async` through callers merely to use a new Process API. For synchronous CLI operations, prefer synchronous overloads, including their timeout parameters. Use asynchronous overloads when responsiveness, scalability, or caller-provided cancellation requires avoiding a blocked calling thread; preserve asynchronous callers and await these overloads directly.

**Concurrent pipe draining does not require asynchronous orchestration.** Synchronous `ReadAllLines`, `ReadAllText`, `ReadAllBytes`, and `RunAndCaptureText` service stdout and stderr together internally and support timeouts. Streaming output, enforcing a timeout, or managing multiple processes is not by itself a reason to introduce asynchronous reader tasks or change method signatures.

Choose the smallest orchestration model that meets the actual concurrency and lifetime requirements. A synchronous loop drains only the process being enumerated: another process that can produce output while that loop runs still needs its own active reader. For example, keep a recorder's existing event-based readers running while synchronously consuming a tracee's output; do not replace the recorder's readers merely for API uniformity. If the calling thread must also service another handshake or feed stdin while draining output, preserve the necessary concurrency rather than serializing those operations.

## Choosing an API

**Do not redirect an inherited stream merely to use a new capture API.**

| Need | Prefer |
|------|--------|
| Start, capture bounded text output, and wait for exit | `RunAndCaptureText[Async]`, including its `ProcessStartInfo` overloads for custom configuration |
| Start and wait without capturing output | `Run[Async]`; use `silent: true` to discard output |
| Inspect or search output lines while the process runs | `ReadAllLines[Async]` on a started process |
| Capture bounded output from an already-started process | `ReadAllText[Async]` for text or `ReadAllBytes[Async]` for binary data, followed by a status wait |
| Discard output with a custom `ProcessStartInfo` | `File.OpenNullHandle()` assigned to `StandardInputHandle`, `StandardOutputHandle`, and `StandardErrorHandle` |
| Forward output to a file without inspecting it | `File.OpenHandle` assigned to `StandardOutputHandle` or `StandardErrorHandle` |
| Pipe one process's output into another process | `SafeFileHandle.CreateAnonymousPipe`, with the write end assigned to the producer's `StandardOutputHandle` and the read end to the consumer's `StandardInputHandle`; dispose the parent's copy of each end right after starting the child that uses it, or the consumer never reaches EOF |
| Wait for termination and obtain the exit status | `WaitForExitStatus[Async]`, or `TryWaitForExitStatus` with a timeout; not `Process.ExitCode` |
| Kill the process on timeout or cancellation, then wait | `process.SafeHandle.WaitForExitOrKillOnTimeout` or `WaitForExitOrKillOnCancellationAsync` |
| Signal a process | `Process.Signal` or `SafeProcessHandle.Signal` with `PosixSignal`. On Windows only `SIGKILL` is supported |
| Limit which handles the child inherits | `ProcessStartInfo.InheritedHandles = []`, listing only the extra handles it needs |
| Create a process with custom P/Invokes | `WindowsProcessStartArguments.Start` or `UnixProcessStartArguments.Start` |
| Start a process without retaining any resources | `Process.StartAndForget` |
| Terminate the child when the parent exits | `ProcessStartInfo.KillOnParentExit` (Windows, Linux, Android) |
| Detach the child from the parent's terminal | `ProcessStartInfo.StartDetached` |
| Manage a process with a small, trimmer-friendly API surface | `SafeProcessHandle.Start` and its wait, kill, and signal methods |

## Reference Guide

Read the references that match the task before applying their guidance; read several when a task spans them. Resolve paths relative to this file. If a required reference cannot be read, report the reduced coverage instead of claiming a complete solution.

| Task | Reference |
|------|-----------|
| Capturing, inspecting, or searching output; avoiding pipe deadlocks; bounding memory | [output capture](references/output-capture.md) |
| Starting and waiting without capturing output, `silent: true`, or discarding output with null handles | [running without capture](references/running-without-capture.md) |
| Redirecting standard handles to files, other processes, or the null device | [standard-handle redirection](references/redirecting-standard-handles.md) |
| Waiting for exit, timeouts, cancellation, exit status, signals, or termination | [waiting and termination](references/waiting-and-termination.md) |
| Restricting inherited handles, inherited pipe writers, or custom native process creation | [handle inheritance](references/handle-inheritance.md) |
| Fire-and-forget starts, disposal, parent-exit termination, or terminal detachment | [lifetime and detachment](references/lifetime-and-detachment.md) |
| Using `SafeProcessHandle` directly, including for NativeAOT | [SafeProcessHandle](references/safe-process-handle.md) |
| Modifying, modernizing, or reviewing existing process code, including searching it for bugs | [anti-patterns](references/anti-patterns.md) |
