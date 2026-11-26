## Run and Wait Without Capturing Output

Use `Process.Run` or `Process.RunAsync` when you need to start a process and wait for its exit status without capturing its output. By default, the child inherits the parent's standard handles, so output remains visible. Pass `silent: true` to redirect all three standard handles (stdin, stdout, and stderr) to the null device (`NUL` on Windows, `/dev/null` on Unix). Writes are discarded and reads receive EOF, without creating pipes that the parent must drain.

Synchronous overloads accept a timeout; asynchronous overloads accept a cancellation token. When the process is killed due to timeout or cancellation, the helper waits for termination and returns a `ProcessExitStatus` with `Canceled` set to `true`, rather than throwing a timeout or cancellation exception.

```csharp
public static ProcessExitStatus Run(string fileName, IEnumerable<string>? arguments = null, bool silent = false, TimeSpan? timeout = null)
public static Task<ProcessExitStatus> RunAsync(string fileName, IEnumerable<string>? arguments = null, bool silent = false, CancellationToken cancellationToken = default)
public static ProcessExitStatus Run(ProcessStartInfo startInfo, TimeSpan? timeout = null)
public static Task<ProcessExitStatus> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
```

```csharp
ProcessExitStatus status = Process.Run("dotnet", ["build"], silent: true);
```

The asynchronous equivalent is:

```csharp
ProcessExitStatus status = await Process.RunAsync("dotnet", ["build"], silent: true);
```

For custom `ProcessStartInfo`, use `File.OpenNullHandle()` and assign `StandardInputHandle`, `StandardOutputHandle`, and `StandardErrorHandle` directly:

```csharp
using System.Diagnostics;
using System.IO;
using Microsoft.Win32.SafeHandles;

using SafeFileHandle nullHandle = File.OpenNullHandle();
ProcessStartInfo startInfo = new("dotnet", ["build"])
{
    StandardInputHandle = nullHandle,
    StandardOutputHandle = nullHandle,
    StandardErrorHandle = nullHandle,
    InheritedHandles = []
};

ProcessExitStatus status = Process.Run(startInfo);
```

In asynchronous code, use `await Process.RunAsync(startInfo)` instead. The child receives its own copy of the handle during process creation. Because `Run[Async]` does not expose a separate start-completed boundary, keep the `using` scope around the call, as shown. You can redirect only selected standard handles to the null device and leave the others inherited; do not enable the corresponding `RedirectStandardInput`, `RedirectStandardOutput`, or `RedirectStandardError` flags.

**Never redirect output to pipes merely to suppress it.** This common anti-pattern creates stdout and stderr pipes without consuming them:

```csharp
ProcessStartInfo psi = new("dotnet", ["build"])
{
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};

using Process process = Process.Start(psi)!;
process.WaitForExit();
```

If either output pipe fills, the child blocks writing while the parent waits for exit. Redirected stdin can also leave the child waiting for input or EOF that the parent never supplies. This pattern **must be avoided**: use `silent: true` or null handles to discard output, and the capture or line-reading APIs when output is needed. Replacing the wait with an asynchronous or status wait does not fix undrained pipes.
