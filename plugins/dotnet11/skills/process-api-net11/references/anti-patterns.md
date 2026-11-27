# Process API Anti-Patterns

Do not write these patterns in new code, and fix every occurrence in existing code. Each entry shows the bad shape, what goes wrong, and the fix.

## Output and Pipes

- `WaitForExit()` before reading redirected stdout or stderr: the child blocks on a full pipe and both processes deadlock. Use `RunAndCaptureText[Async]`, or `ReadAllText[Async]`/`ReadAllLines[Async]` followed by a status wait.
- `StandardOutput.ReadToEnd()` followed by `StandardError.ReadToEnd()`, or sequentially awaited `ReadToEndAsync` calls: deadlocks when the child fills the other pipe. Read both streams concurrently with the APIs above.
- Writing all of stdin before draining redirected stdout and stderr: deadlocks once an output pipe fills. Drain output concurrently, or supply the input from a file through `StandardInputHandle = File.OpenHandle(...)`.
- `RedirectStandard*` set only to suppress output, stdin redirected only to be closed, or redirected stdin never closed: unread pipes deadlock, and the child can wait forever for input. Use `silent: true` or `File.OpenNullHandle()`, or leave the stream inherited.
- Reading output to the end only to discard it: wasted work and buffering. Use `silent: true` or null handles.
- Redirecting stdout and stderr only to write the captured text unchanged to `Console.Out` and `Console.Error`: adds pipes that must be drained, buffers all output, delays it until capture completes, and loses the interleaving of the two streams. Use `Run[Async]` without redirection so the child writes directly to the inherited streams.
- `OutputDataReceived`/`ErrorDataReceived` with `Begin*ReadLine` appending to a shared `StringBuilder` or `string`: appends race without a lock, the final `null` `e.Data` adds an empty line, and the buffer is often read before EOF. Use `ReadAllText[Async]` or `ReadAllLines[Async]`; keep event readers only when output must be observed while the parent does other work.
- `WaitForExit(timeout)` followed by a parameterless `WaitForExit()` "to flush output", or parameterless `WaitForExit[Async]()` with event readers: these wait for output EOF, so a descendant holding a pipe hangs them. Use `WaitForExitStatus[Async]`.
- Waiting for output EOF before killing a timed-out child: the timeout never takes effect. Kill first, then wait for termination.
- Treating output EOF as process exit, such as reading to the end and then reading `ExitCode`: the child may still be running and `ExitCode` throws. Follow every read with a status wait.

## Waiting and Termination

- Ignoring the `bool` returned by `WaitForExit(milliseconds)` and then reading `ExitCode` or output: throws or reports a process that is still running. Use `TryWaitForExitStatus` and handle `false`.
- `WaitForExit()` or `WaitForExitAsync()` followed by `process.ExitCode`: use `WaitForExitStatus[Async]` and the returned `status.ExitCode`.
- Polling `HasExited` or `WaitForExit(0)` with `Thread.Sleep` or `Task.Delay`: adds latency and races with exit. Use a status wait.
- `EnableRaisingEvents` with an `Exited` handler and a `TaskCompletionSource`, `Task.Delay`, or `token.Register` to implement exit, timeout, or cancellation: use `WaitForExitStatusAsync` or `WaitForExitOrKillOnCancellationAsync`.
- An `Exited` handler that reads `ExitCode` into a field or disposes the `Process`: it runs on another thread, can misreport the exit code, and can dispose a process still in use. Wait for exit and read the status in the owning method.
- A timeout or cancellation that only stops waiting, or `Close()`/`Dispose()` as the timeout action: the child keeps running. Use `WaitForExitOrKillOnTimeout` or `WaitForExitOrKillOnCancellationAsync` when the child must not outlive the wait.
- `Kill()` without a following wait: the process may still be running, holding files, and has no exit code yet. Wait for termination after killing.
- Killing only the direct child when the work runs in its descendants, such as a `dotnet` launcher: use `Kill(entireProcessTree: true)` followed by a wait. `WaitForExitOrKill*`, `Run[Async]`, and `RunAndCaptureText[Async]` kill only the direct child, so keep an existing tree kill.
- Ignoring `ProcessExitStatus.Canceled` after `Run[Async]` or `RunAndCaptureText[Async]` with a timeout or token, or reporting it as an ordinary nonzero exit: report the timeout or cancellation explicitly.
- Checking `HasExited` or `WaitForExit(0)` before `Kill` or `Signal`: the process can exit in between. Call `Kill` or `Signal` directly; `Signal` returns `false` if the process already exited.
- Applying the full timeout to the read and again to the status wait: the total time doubles. Share one deadline and pass the remaining time to the wait.

## Async Code

- `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on reader tasks or process waits, including in eagerly evaluated assertion message arguments: blocks the thread and can deadlock. Use synchronous APIs in synchronous code and await in asynchronous code.
- Synchronous `WaitForExit`, `ReadToEnd`, or `Thread.Sleep` polling inside `async` methods, or a catch-all that swallows `OperationCanceledException`: blocks threads and ignores cancellation. Await the asynchronous APIs with the caller's token.
- Starting `ReadAllTextAsync` or other reads without the cancellation token: after a timeout, the read can wait forever on a pipe held by a descendant. Pass the token.

## Lifetime and Resources

- `Process` objects from `Process.Start`, `new Process`, or `Process.GetProcessById` that are never disposed: leaks handles. Use `using`; disposal does not kill the child.
- Disposing a `Process` while `Begin*ReadLine` readers still run: output draining stops and output is lost.
- Calling instance `Start()` on the `Process` returned by static `Process.Start`: closes the first child's handle and starts a second process, orphaning the first. Remove the second call.
- Hand-maintained lists of child processes to kill at shutdown: use `KillOnParentExit` where supported.
- Long-lived background processes that inherit the parent's standard handles: they keep the parent's stdout and stderr pipes open, delaying EOF for `| tee` or CI log capture. Assign null handles or use `StartDetached`.

## Start Configuration

- `Environment.SetEnvironmentVariable` in the parent to configure a child: changes the parent and races with other children. Set `ProcessStartInfo.Environment`.
- Copying the parent environment into `ProcessStartInfo.Environment`, or assuming it starts empty: it is already initialized from the parent. Apply only the child-specific changes and explicitly remove variables that must not be inherited.
- No `InheritedHandles`: the child inherits every inheritable parent handle. Set `InheritedHandles`.
- A custom `CreateProcess` P/Invoke used only to block inheritance or null the standard handles: use `InheritedHandles` and `File.OpenNullHandle()`.
- Concatenated, interpolated, or hand-escaped `Arguments` strings: use argument collections.
- Explicit `UseShellExecute = false`: leave the property unset.
- `Process.Start(...) ?? throw`, `is null` checks, `if (!process.Start())`, or `p == null || p.HasExited` as launch-failure checks for direct launches: `Process.Start` returns a process or throws.
- Manual `Start`, `WaitForExit`, and `ExitCode` when only the exit code is needed: use `Process.Run[Async]`.
- Starting `chmod`, `ls`, `kill`, or `whoami` for something a managed API does: use `File.SetUnixFileMode`/`File.GetUnixFileMode`, `Process.Signal` or `Kill`, or `Environment.UserName`.
- `Process.GetCurrentProcess().MainModule.FileName` for the current executable: use `Environment.ProcessPath`.

## Not Anti-Patterns

Keep these when you find them:

- `WaitForExitAsync` for a process obtained by `Process.GetProcessById`: status waits cannot wait for a non-child process on Unix.
- Reading a single redirected stream to the end before waiting: one pipe cannot deadlock this way.
- Event readers that must observe output while the parent does other work, such as handshakes or readiness signals.
- An existing `Kill(entireProcessTree: true)`.
- Code for older target frameworks: keep it, and put .NET 11 APIs under `#if NET11_0_OR_GREATER`.
