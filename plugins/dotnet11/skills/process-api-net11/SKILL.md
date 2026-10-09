---
name: process-api-net11
description: >
  Provides guidance on the new System.Diagnostics.Process APIs introduced in .NET 11:
  Process.Run, Process.RunAndCaptureText, Process.StartAndForget, Process.ReadAllText/Bytes/Lines,
  KillOnParentExit, InheritedHandles, and StartDetached.
  USE FOR: starting, orchestrating, or capturing output from external processes in applications targeting net11.0 or later.
  DO NOT USE FOR: applications targeting .NET 10 or earlier, or basic pre-.NET 11 Process.Start usage without new APIs.
license: MIT
---

# Process API Improvements — .NET 11

New APIs added to `System.Diagnostics.Process` in .NET 11 simplify process management, eliminate boilerplate, and prevent common deadlock patterns when capturing output in .NET 11 or later applications.

## When to Use

- Running or orchestrating external processes in a .NET 11 (or later) project.
- Needing to start a process, wait for it to exit, and capture its output/error streams without risking deadlocks (`Process.RunAndCaptureText[Async]`).
- Wanting to ensure child processes are automatically terminated when the parent process exits (`KillOnParentExit`).
- Requiring lightweight, low-overhead process creation via `SafeProcessHandle` on supported platforms.
- Requiring fine-grained control over handle inheritance (`InheritedHandles`) or starting detached processes (`StartDetached`).

## When Not to Use

- The project targets .NET 10 or earlier — these APIs are not available before .NET 11.
- The default `Process.Start()` is sufficient and does not require output capturing or advanced lifecycle rules.

## Target Framework

```xml
<TargetFramework>net11.0</TargetFramework>
```

## New APIs

### Types

Before using the new convenience methods, note the following types. Use the framework types directly:

| Type | Kind | Read-only properties |
|------|------|----------------------|
| `ProcessExitStatus` | Sealed class | `int ExitCode`, `bool Canceled`, `PosixSignal? Signal` |
| `ProcessTextOutput` | Sealed class | `ProcessExitStatus ExitStatus`, `string StandardOutput`, `string StandardError`, `int ProcessId` |
| `ProcessOutputLine` | Readonly struct | `string Content`, `bool StandardError` |

`ProcessExitStatus` describes the outcome of a completed process. Interpret `ExitCode` using the external command's contract; there is no universal `Success` property. `ProcessTextOutput` contains the exit status, captured output and error text, and process ID. `ProcessOutputLine` identifies one output line and whether it came from standard error.

### High-Level Convenience APIs

#### Static Methods

The `ProcessStartInfo` overloads below require `UseShellExecute = false`, which is the default for .NET.

##### `Process.Run` / `Process.RunAsync`
Starts a process and waits for it to exit, returning the exit status. Does not capture standard output or error. Passing `silent: true` discards standard output and error by internally redirecting standard handles to the `NUL` device. On timeout or cancellation, the process is killed.
```csharp
public static ProcessExitStatus Run(string fileName, IEnumerable<string>? arguments = null, bool silent = false, TimeSpan? timeout = null)
public static Task<ProcessExitStatus> RunAsync(string fileName, IEnumerable<string>? arguments = null, bool silent = false, CancellationToken cancellationToken = default)
public static ProcessExitStatus Run(ProcessStartInfo startInfo, TimeSpan? timeout = null)
public static Task<ProcessExitStatus> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
```

##### `Process.RunAndCaptureText` / `Process.RunAndCaptureTextAsync`
Starts a process, captures both standard output and error, and waits for it to exit. Extremely useful for avoiding deadlocks on stream redirection. On timeout or cancellation, the process is killed.

When using the `ProcessStartInfo` overloads, the caller is responsible for setting `RedirectStandardOutput = true` and `RedirectStandardError = true` on `ProcessStartInfo` (because BCL APIs cannot modify the input arguments they were given).
```csharp
public static ProcessTextOutput RunAndCaptureText(string fileName, IEnumerable<string>? arguments = null, TimeSpan? timeout = null)
public static Task<ProcessTextOutput> RunAndCaptureTextAsync(string fileName, IEnumerable<string>? arguments = null, CancellationToken cancellationToken = default)
public static ProcessTextOutput RunAndCaptureText(ProcessStartInfo startInfo, TimeSpan? timeout = null)
public static Task<ProcessTextOutput> RunAndCaptureTextAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
```

##### `Process.StartAndForget`
There is a common misconception that when a process is disposed, it's also being killed. This is not the case, as `Process.Dispose` only releases the resources associated with the process, but does not kill it.

To make it easier to start a process without the need to worry about disposing it, `Process.StartAndForget` was introduced. The method starts an executable, returns its ID, and immediately releases all handle resources associated with it. Standard handles not supplied through `StandardInputHandle`, `StandardOutputHandle`, or `StandardErrorHandle` go to the null device by default.

`StartAndForget` throws `InvalidOperationException` if `UseShellExecute` or any `RedirectStandardInput`, `RedirectStandardOutput`, or `RedirectStandardError` flag is true. Use a capture or streaming API if output needs to be read.
```csharp
public static int StartAndForget(string fileName, IEnumerable<string>? arguments = null)
public static int StartAndForget(ProcessStartInfo startInfo)
```

#### Instance Methods
These methods are called on a `Process` instance to directly read stdout and stderr, guaranteeing no OS pipe buffer overflow deadlocks.

*Note: Calling these methods requires `RedirectStandardOutput = true` and `RedirectStandardError = true` on the `ProcessStartInfo` passed to `Process.Start`.*

```csharp
public (string StandardOutput, string StandardError) ReadAllText(TimeSpan? timeout = null)
public Task<(string StandardOutput, string StandardError)> ReadAllTextAsync(CancellationToken cancellationToken = default)
public (byte[] StandardOutput, byte[] StandardError) ReadAllBytes(TimeSpan? timeout = null)
public Task<(byte[] StandardOutput, byte[] StandardError)> ReadAllBytesAsync(CancellationToken cancellationToken = default)
public IEnumerable<ProcessOutputLine> ReadAllLines(TimeSpan? timeout = null)
public IAsyncEnumerable<ProcessOutputLine> ReadAllLinesAsync(CancellationToken cancellationToken = default)
```

Example of deconstructing the tuple return:
```csharp
var (stdout, stderr) = await process.ReadAllTextAsync();
```

### ProcessStartInfo Properties

#### `KillOnParentExit`
Ensures that the spawned child process is terminated when the current (parent) process exits (including fatal crash and being force killed). Works across Windows, Linux, and Android.
```csharp
public bool KillOnParentExit { get; set; }
```

In cross-platform code, guard the property assignment with a supported-platform check, not only the assigned value. If automatic teardown is required, fail explicitly on an unsupported platform instead of starting an unprotected child process.
`KillOnParentExit` requires `UseShellExecute = false`.

#### `InheritedHandles`
Provides precise control over which handles (file descriptors) are inherited by the child process, preventing accidental resource leaks.
- Standard handles (`stdin`, `stdout`, `stderr`) are always included (no need to add them to the list).
- Setting the list to an empty list means only standard handles get inherited.
- Only `SafeFileHandle` and `SafePipeHandle` instances are allowed as of today.
- No global lock is used when spawning new processes on Windows (important for tuning projects that spawn multiple processes in parallel).
- Concurrent process starts must not pass the same handle in `InheritedHandles`: the runtime temporarily changes its inheritance flags. Serialize starts that share a handle, or use separate handles for each concurrent start.
- Do not enable inheritance on the handles before passing them; other process-start APIs could then inherit them unintentionally.
- On Unix systems without native handle-inheritance control, setting this property can severely reduce process-start performance.
- A non-null list requires `UseShellExecute = false` and cannot be combined with a non-empty `UserName`.
```csharp
public IList<SafeHandle>? InheritedHandles { get; set; }
```

#### `StartDetached`
Starts the process detached from the parent's terminal or job session, ensuring it survives the parent's exit.
```csharp
public bool StartDetached { get; set; }
```

`StartDetached` requires `UseShellExecute = false`.

---

## Examples

### 1. One-Line Run and Capture Output

Run a process and safely read all output text without stream deadlock risks. For simple CLI operations that don't need cancellation or async scalability, prefer the synchronous overload:

```csharp
using System;
using System.Diagnostics;

// Run 'git status' and capture output
ProcessTextOutput result = Process.RunAndCaptureText("git", ["status"]);

if (result.ExitStatus.ExitCode == 0)
{
    Console.WriteLine($"Git Output: {result.StandardOutput}");
}
else
{
    Console.WriteLine($"Failed with exit code: {result.ExitStatus.ExitCode}");
    Console.WriteLine($"Error: {result.StandardError}");
}
```

### 2. Auto-Killing Child Processes on Parent Exit

On Windows, Linux, and Android, ensure a long-running background worker process is killed when the main application terminates. On other platforms, this example stops before starting the child process:

```csharp
using System;
using System.Diagnostics;

ProcessStartInfo startInfo = new("dotnet", ["run", "--project", "BackgroundWorker.csproj"]);

if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
{
    startInfo.KillOnParentExit = true;
}
else
{
    throw new PlatformNotSupportedException("This example requires Windows, Linux, or Android.");
}

using Process process = Process.Start(startInfo)!;
// The background worker is now tied to this process's lifecycle
```

### 3. Read All Lines From Output

Start a process and read its output lines safely:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;

ProcessStartInfo startInfo = new("ping", ["127.0.0.1"])
{
    RedirectStandardOutput = true,
    RedirectStandardError = true
};

using Process process = Process.Start(startInfo)!;
// Read all output lines safely and asynchronously
await foreach (ProcessOutputLine line in process.ReadAllLinesAsync())
{
    string prefix = line.StandardError ? "[Err]" : "[Out]";
    Console.WriteLine($"{prefix} > {line.Content}");
}
```

### 4. Read Entire Output Deadlock-Free (Tuple Return)

Start a process with custom `ProcessStartInfo` and read both stdout and stderr into a deconstructed tuple without buffer deadlocks:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;

ProcessStartInfo startInfo = new("git", ["diff"])
{
    RedirectStandardOutput = true,
    RedirectStandardError = true
};

using Process process = Process.Start(startInfo)!;
var (stdout, stderr) = await process.ReadAllTextAsync();

Console.WriteLine($"Diff output: {stdout}");
```

### 5. Start and Forget (Fire & Forget)

Launch a helper executable without holding onto system handle structures:

```csharp
using System;
using System.Diagnostics;

// Fire and forget, getting back only the process ID
int pid = Process.StartAndForget("notepad.exe");
Console.WriteLine($"Notepad started with PID: {pid}");
```
