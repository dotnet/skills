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
  SafeProcessHandle and trimmer-friendly process management for NativeAOT applications.
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

When starting a process with the APIs or `ProcessStartInfo` features described in this skill, keep `UseShellExecute` at its default value of `false`. Leave the property unset rather than explicitly assigning `false`: when the application never sets it, the AOT compiler can eliminate the unused shell-execution code.

**Prefer argument collections over concatenating, interpolating, or joining arguments into `ProcessStartInfo.Arguments`.**

Do not manually copy the parent's environment variables: `ProcessStartInfo.Environment` and `EnvironmentVariables` already initialize from them automatically. Apply only the child-specific changes.

The nullability of static `Process.Start` depends on the launch mode:

| Launch mode | Return value and handling |
|-------------|---------------------------|
| Direct executable launch: `UseShellExecute = false` (the default) | Returns a `Process` on success or throws. Use `Process.Start(startInfo)!`; no redundant null check or `?? throw` is needed. |
| Shell association: `UseShellExecute = true` | May return `null` after successfully opening a URL or document without creating a new process. Preserve the nullable result; do not apply the direct-launch non-null guarantee or treat `null` alone as a launch failure. |

The `!` assertions in the executable examples below apply only with shell execution disabled. If a shell-opening request brought you here, do not replace it with these executable-management APIs. Ordinary `Process.Start` with `UseShellExecute = true` returns without waiting; use `Process?` or null-conditional disposal for any returned wrapper, without waiting for or terminating the browser or document application.

## Choosing Synchronous or Asynchronous APIs

When modernizing existing code, preserve its synchronous or asynchronous calling model unless a concrete requirement justifies changing it. Do not propagate `async` through callers merely to use a new Process API. For synchronous CLI operations, prefer synchronous overloads, including their timeout parameters. Use asynchronous overloads when responsiveness, scalability, or caller-provided cancellation requires avoiding a blocked calling thread; preserve asynchronous callers and await these overloads directly.

**Concurrent pipe draining does not require asynchronous orchestration.** Synchronous `ReadAllLines`, `ReadAllText`, `ReadAllBytes`, and `RunAndCaptureText` service stdout and stderr together internally and support timeouts. Streaming output, enforcing a timeout, or managing multiple processes is not by itself a reason to introduce asynchronous reader tasks or change method signatures.

Choose the smallest orchestration model that meets the actual concurrency and lifetime requirements. A synchronous loop drains only the process being enumerated: another process that can produce output while that loop runs still needs its own active reader. For example, keep a recorder's existing event-based readers running while synchronously consuming a tracee's output; do not replace the recorder's readers merely for API uniformity. If the calling thread must also service another handshake or feed stdin while draining output, preserve the necessary concurrency rather than serializing those operations.

**Never use `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()` to synchronously block on an asynchronous process operation: sync-over-async can deadlock and blocks the calling thread.** Do not call synchronous process-wait methods from asynchronous code either.

## Choosing an API

**Do not redirect an inherited stream merely to use a new capture API.**

| Need | Prefer |
|------|--------|
| Start, capture bounded text output, and wait for exit | `RunAndCaptureText[Async]`, including its `ProcessStartInfo` overloads for custom configuration |
| Start and wait without capturing output | `Run[Async]`; use `silent: true` to discard output |
| Inspect or search output lines while the process runs | `ReadAllLines[Async]` on a started process |
| Capture bounded output from an already-started process | `ReadAllText[Async]` for text or `ReadAllBytes[Async]` for binary data, followed by a status wait |
| Forward output to a file or another process without inspecting it | Standard-handle redirection, without parent-side capture |
