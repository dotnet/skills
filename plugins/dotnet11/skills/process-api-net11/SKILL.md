---
name: process-api-net11
description: >
  Provides guidance on the new System.Diagnostics.Process APIs introduced in .NET 11:
  Process.Run, Process.RunAndCaptureText, Process.StartAndForget, Process.ReadAllText/Bytes/Lines,
  KillOnParentExit, InheritedHandles, and StartDetached.
  USE FOR: starting, orchestrating, or capturing output from external processes in .NET 11 applications (net11.0).
  DO NOT USE FOR: applications targeting .NET 10 or earlier, or basic pre-.NET 11 Process.Start usage without new APIs.
license: MIT
---

# Process API Improvements — .NET 11

New APIs added to `System.Diagnostics.Process` in .NET 11 simplify process management, eliminate boilerplate, and prevent common deadlock patterns when capturing output.

## When to Use

- Running or orchestrating external processes in a .NET 11 (or later) project.
- Needing to start a process, wait for it to exit, and capture its output/error streams without risking deadlocks (`Process.RunAndCaptureText[Async]`).
- Wanting to ensure child processes are automatically terminated when the parent process exits (`KillOnParentExit`).
- Requiring lightweight, low-overhead process creation via `SafeProcessHandle` for better performance characteristics across all form-factors and runtimes.
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

Before using the new convenience methods, note the following structures:

- **`ProcessExitStatus`**: Represents the outcome of a completed process.
  ```csharp
  public readonly record struct ProcessExitStatus(int ExitCode, bool Canceled, PosixSignal? Signal = null);
  ```
- **`ProcessTextOutput`**: Contains the exit status along with all captured standard output, standard error text, and the process ID.
  ```csharp
  public readonly record struct ProcessTextOutput(ProcessExitStatus ExitStatus, string StandardOutput, string StandardError, int ProcessId);
  ```
- **`ProcessOutputLine`**: Represents a single output line tagged with its stream source.
  ```csharp
  public readonly struct ProcessOutputLine
  {
      public string Content { get; }
      public bool StandardError { get; }
  }
  ```

### High-Level Convenience APIs

#### Static Methods

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

To make it easier to start a process without the need to worry about disposing it, `Process.StartAndForget` was introduced. The method starts a process, returns its ID, and immediately releases all handle resources associated with it. By default, when output/error redirection was not specified, `Process.StartAndForget` redirects all standard handles to the `NUL` device.
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

#### `InheritedHandles`
Provides precise control over which handles (file descriptors) are inherited by the child process, preventing accidental resource leaks.
- Standard handles (`stdin`, `stdout`, `stderr`) are always included (no need to add them to the list).
- Setting the list to an empty list means only standard handles get inherited.
- Only `SafeFileHandle` and `SafePipeHandle` instances are allowed as of today.
- No global lock is used when spawning new processes on Windows (important for tuning projects that spawn multiple processes in parallel).
```csharp
public IList<SafeHandle>? InheritedHandles { get; set; }
```

#### `StartDetached`
Starts the process detached from the parent's terminal or job session, ensuring it survives the parent's exit.
```csharp
public bool StartDetached { get; set; }
```

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

On Windows and Linux, ensure a long-running background worker process is killed when the main application terminates. This example does not enable automatic teardown on other platforms:

```csharp
using System;
using System.Diagnostics;

ProcessStartInfo startInfo = new("dotnet", ["run", "--project", "BackgroundWorker.csproj"])
{
    KillOnParentExit = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() // Auto-teardown when this parent process exits
};

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

Launch a helper tool or browser without holding onto system handle structures:

```csharp
using System;
using System.Diagnostics;

// Fire and forget, getting back only the process ID
int pid = Process.StartAndForget("notepad.exe");
Console.WriteLine($"Notepad started with PID: {pid}");
```
