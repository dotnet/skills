## Using SafeProcessHandle for a Smaller, Trimmer-Friendly API Surface

Prefer `Microsoft.Win32.SafeHandles.SafeProcessHandle` when you need a process handle for starting, waiting, terminating, or signaling, but do not need `Process` stream readers, output events, or its broader component-based API. Its focused surface avoids the higher-level `Process` wrapper and offers better trimmability, making it useful for size-sensitive NativeAOT applications. The resulting binary-size savings depend on the application and platform; they are not a fixed percentage.

For a combined start-and-wait operation without output capture, keep preferring `Process.Run[Async]`: it is simpler and uses `SafeProcessHandle` internally.

```csharp
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

ProcessStartInfo startInfo = new("whoami")
{
    InheritedHandles = []
};

using SafeProcessHandle handle = SafeProcessHandle.Start(startInfo);
ProcessExitStatus status = handle.WaitForExit();
```

Standard handles are inherited by default, so this example leaves output visible. Keep `UseShellExecute` unset, as described above, to allow unused shell-execution code to be trimmed as well.

| Need | SafeProcessHandle API |
|------|-----------------------|
| Start a process and obtain its ID | `Start(ProcessStartInfo)` returns an owning handle; read its `ProcessId` |
| Wait for termination and obtain its status | `WaitForExit()` or `WaitForExitAsync(cancellationToken)` |
| Wait with a timeout without killing | `TryWaitForExit(timeout, out ProcessExitStatus? exitStatus)` |
| Kill on timeout or cancellation, then wait | `WaitForExitOrKillOnTimeout(timeout)` or `WaitForExitOrKillOnCancellationAsync(cancellationToken)` |
| Request forceful termination | `Kill()`; follow with a wait when termination must be confirmed |
| Send a Unix signal | `Signal(PosixSignal)`; on Windows only `SIGKILL` is supported |

Unlike the legacy `Process.WaitForExit[Async]` methods, the handle's waits observe termination only; they do not wait for output EOF. Canceling `handle.WaitForExitAsync` stops the wait without killing the child. For the kill-on-timeout or cancellation alternatives and their returned `Canceled` status, see [Choose Whether Cancellation or Timeout Kills the Process](#choose-whether-cancellation-or-timeout-kills-the-process).

`SafeProcessHandle.Start` does not expose managed standard streams and throws if any `RedirectStandardInput`, `RedirectStandardOutput`, or `RedirectStandardError` flag is enabled. Use the `StandardInputHandle`, `StandardOutputHandle`, and `StandardErrorHandle` properties for direct handle redirection, or keep using `Process` when its capture or line-reading APIs are needed. Do not replace those APIs with manual pipe plumbing merely to avoid the wrapper.

Dispose handles returned by `Start`, normally with `using`; disposal releases parent-side resources, not the child process. If you already have a `Process`, its `SafeHandle` property exposes these operations without starting another process. That handle belongs to the `Process`; do not dispose it independently while continuing to use the wrapper. For custom native process creation, use the coordinated entry points described in [Custom P/Invoke Process Creation](#custom-pinvoke-process-creation).
