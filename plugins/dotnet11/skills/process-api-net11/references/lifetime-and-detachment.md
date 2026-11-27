## Starting a Process Without Retaining Its Resources

There is a common misconception that when a process is disposed, it's also being killed. `Process` implements `IDisposable`, but `Process.Dispose` only releases the resources associated with the process; it does not kill it. Dispose it as soon as you no longer need to interact with the process or read its output, typically with a `using` statement, to release the resources held by the wrapper.

To make it easier to start a process without the need to worry about disposing it, `Process.StartAndForget` was introduced. The method starts an executable, returns its ID, and immediately releases all handle resources associated with it. Standard handles not supplied through `StandardInputHandle`, `StandardOutputHandle`, or `StandardErrorHandle` go to the null device by default.

`StartAndForget` throws `InvalidOperationException` if any `RedirectStandardInput`, `RedirectStandardOutput`, or `RedirectStandardError` flag is true. Use a capture or streaming API if output needs to be read.

`StartAndForget` also throws `InvalidOperationException` when `UseShellExecute` is `true`. It launches executables, not URLs or documents through shell associations. For shell opening, use ordinary `Process.Start` with `UseShellExecute = true`; do not substitute `StartAndForget` merely because the caller should continue immediately. `Process.Start` does not wait for the launched application to exit.

```csharp
public static int StartAndForget(string fileName, IEnumerable<string>? arguments = null)
public static int StartAndForget(ProcessStartInfo startInfo)
```

## Terminating Child Processes When the Parent Exits

`ProcessStartInfo.KillOnParentExit` ensures that the spawned child process is terminated when the current (parent) process exits (including fatal crash and being force killed). It's supported on Windows, Linux, and Android.

When the caller's operating system is unspecified, state this supported-platform scope or guard it before starting the child. Reject unsupported platforms rather than silently launching a child without the requested protection.

```csharp
public class ProcessStartInfo
{
    public bool KillOnParentExit { get; set; }
}
```

On Windows, job membership is inherited by default, so `KillOnParentExit` applies to the child's process tree, not just the direct child. Descendants explicitly created to break away from the job are excluded.

On Linux and Android, process starts with `KillOnParentExit` enabled are serialized, even when requested concurrently.

## Detaching a Process From the Parent's Terminal

`ProcessStartInfo.StartDetached` starts the process without attachment to the parent's console on Windows, or in a new session without a controlling terminal on Unix.

Terminal detachment does not guarantee that the child survives the parent: `StartDetached` does not override `KillOnParentExit` or escape a Windows job's lifetime restrictions.

When `StartDetached` is `true`, standard handles without explicit redirection are redirected to the null device to avoid keeping the parent's standard handles open unnecessarily.

```csharp
public bool StartDetached { get; set; }
```
