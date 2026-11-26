## Waiting for Process Exit

### Modernize the Behavioral Contract, Not Individual Calls

Before modernizing process-management code, identify its behavioral contract: what it waits for, what triggers termination, which processes it terminates, and how it reports completion or failure. Evaluate the entire mechanism, even when spread across callbacks, helpers, or multiple methods, against the new APIs. Recognize intent rather than matching particular code patterns.

When the intent is "wait for this process to exit, terminate it on timeout or cancellation, and wait for termination," use `SafeProcessHandle.WaitForExitOrKillOnCancellationAsync` for asynchronous code or `WaitForExitOrKillOnTimeout` for synchronous code. For an asynchronous timeout, supply a token from a timeout-backed `CancellationTokenSource`. Replace the equivalent custom orchestration rather than merely modernizing individual calls within it. Preserve required logging, exception behavior, and cleanup scope.

Retain custom orchestration only when a concrete requirement is not provided by the new API; identify that requirement explicitly.

### Wait for Termination, Not Output EOF

Prefer the .NET 11 status-wait methods when waiting for a process to terminate. They return its exit status without waiting for stdout or stderr to reach EOF.

The existing parameterless `Process.WaitForExit()` and `Process.WaitForExitAsync()` also wait for EOF from redirected streams being read through `BeginOutputReadLine` or `BeginErrorReadLine`. A grandchild can inherit a pipe's write handle and keep it open after the child exits, leaving these waits stuck even though the child has terminated. See [dotnet/runtime#51277](https://github.com/dotnet/runtime/issues/51277). Finite-timeout `WaitForExit` overloads do not wait for those readers to finish; do not assume all overloads have the same output-completion behavior.

The new status waits do not read or drain output. For redirected streams, use `ReadAllText[Async]`, `ReadAllBytes[Async]`, or `ReadAllLines[Async]` to drain stdout and stderr concurrently while the process runs.

```csharp
// Process instance methods
public ProcessExitStatus WaitForExitStatus()
public bool TryWaitForExitStatus(TimeSpan timeout, out ProcessExitStatus? exitStatus)
public Task<ProcessExitStatus> WaitForExitStatusAsync(CancellationToken cancellationToken = default)
```

Use `WaitForExitStatus` for synchronous waiting and `WaitForExitStatusAsync` in asynchronous code. `TryWaitForExitStatus` returns `false` if the timeout expires without terminating the process; only use its `exitStatus` output when it returns `true`.

### Do Not Use Process.ExitCode as a Wait

`Process.ExitCode` is a synchronous property, not an asynchronous completion operation. It checks process state and throws `InvalidOperationException` if the process is still running. Reading all output does not prove that it has exited. Do not poll this property or use it instead of waiting for termination.

In asynchronous code, await `WaitForExitStatusAsync` directly and read the returned object's properties:

```csharp
ProcessExitStatus status = await process.WaitForExitStatusAsync();
int exitCode = status.ExitCode;
```

`status.ExitCode` is an already-obtained value, unlike the process-state checks performed by `process.ExitCode`. Use the framework's sealed `ProcessExitStatus` class directly; its read-only properties are `int ExitCode`, `bool Canceled`, and `PosixSignal? Signal` for the terminating signal on Unix. Interpret exit codes using the external command's contract; there is no universal `Success` property.

### Choose Whether Cancellation or Timeout Kills the Process

Canceling `Process.WaitForExitStatusAsync(cancellationToken)` stops the wait with `OperationCanceledException`; it does not kill the process. Use that policy only when the child is intentionally allowed to continue and its lifetime is managed elsewhere.

When managing the child's lifetime with a cancellation token, prefer `SafeProcessHandle.WaitForExitOrKillOnCancellationAsync`, available through `process.SafeHandle`. It kills the process when cancellation is requested and then waits for termination, rather than abandoning a running child. For a synchronous timeout with the same cleanup policy, use `WaitForExitOrKillOnTimeout`.

```csharp
// SafeProcessHandle instance methods
public Task<ProcessExitStatus> WaitForExitOrKillOnCancellationAsync(CancellationToken cancellationToken)
public ProcessExitStatus WaitForExitOrKillOnTimeout(TimeSpan timeout)
```

For a started process and a caller-provided cancellation token:

```csharp
ProcessExitStatus status =
    await process.SafeHandle.WaitForExitOrKillOnCancellationAsync(cancellationToken);
```

For a synchronous timeout:

```csharp
ProcessExitStatus status =
    process.SafeHandle.WaitForExitOrKillOnTimeout(TimeSpan.FromSeconds(30));
```

Both return the exit status after termination, with `Canceled` set when the process was killed due to cancellation or timeout. They do not merely throw a cancellation or timeout exception, and the timeout is the time before killing, not a bound on the subsequent termination wait. These methods terminate the associated process, not an entire descendant tree.

If also capturing redirected output, start the kill-on-cancellation wait before awaiting output capture so cancellation can kill a child even while a read is pending. Manage output-read cancellation separately: process termination still does not guarantee EOF if another process retains a pipe writer.

### Request Graceful Shutdown With a Signal

Send a signal with `Process.Signal(PosixSignal)` or `SafeProcessHandle.Signal(PosixSignal)`; on Windows only `SIGKILL` is supported.

On Unix, `Signal(PosixSignal.SIGTERM)` can request graceful shutdown; the child may handle or ignore it. Follow it with `TryWaitForExit` (`TryWaitForExitStatus` on `Process`) for a bounded grace period, then `Kill` and a wait if forceful termination is required. `PosixSignal` is in `System.Runtime.InteropServices`. `Signal` returns `false` if the process has already exited or does not exist.
