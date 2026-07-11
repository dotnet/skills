---
name: process-api-net11
description: >
  Provides guidance on the new System.Diagnostics.Process APIs introduced in .NET 11.
  It covers high-level convenience methods (Process.Run, Process.RunAndCaptureText, Process.StartAndForget),
  reliable deadlock-free output reading (Process.ReadAllText/Bytes/Lines), and lifecycle/handle management
  (KillOnParentExit, InheritedHandles, StartDetached).
  Use when starting, orchestrating, or capturing output from external processes in .NET 11 applications.
license: MIT
---

# Process API Improvements — .NET 11

New APIs added to `System.Diagnostics.Process` in .NET 11 simplify process management, eliminate boilerplate, and prevent common deadlock patterns when capturing output.

## When to Use

- Running or orchestrating external processes in a .NET 11 (or later) project.
- Needing to start a process, wait for it to exit, and capture its output/error streams without risking deadlocks (`Process.RunAndCaptureTextAsync`).
- Wanting to ensure child processes are automatically terminated when the parent process exits (`KillOnParentExit`).
- Requiring trimmer-friendly and NativeAOT-compatible process creation via `SafeProcessHandle`.
- Requiring fine-grained control over handle inheritance (`InheritedHandles`) or starting detached processes (`StartDetached`).

## When Not to Use

- The project targets .NET 10 or earlier — these APIs are not available before .NET 11.
- Running simple shells where custom execution code is unnecessary.
- The default `Process.Start()` is sufficient and does not require output capturing or advanced lifecycle rules.

## Target Framework

```xml
<TargetFramework>net11.0</TargetFramework>
```

## New APIs & Convenience Methods

### High-Level Convenience APIs (Static Methods)

#### `Process.Run` / `Process.RunAsync`
Starts a process and waits for it to exit, returning the exit status. Does not capture standard output or error.
```csharp
public static ProcessExitStatus Run(string fileName, IList<string>? arguments = null, bool silent = false, TimeSpan? timeout = null)
public static Task<ProcessExitStatus> RunAsync(string fileName, IList<string>? arguments = null, bool silent = false, CancellationToken cancellationToken = default)
public static ProcessExitStatus Run(ProcessStartInfo startInfo, TimeSpan? timeout = null)
public static Task<ProcessExitStatus> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
```

#### `Process.RunAndCaptureText` / `Process.RunAndCaptureTextAsync`
Starts a process, captures both standard output and error, and waits for it to exit. Extremely useful for avoiding deadlocks on stream redirection.
```csharp
public static ProcessTextOutput RunAndCaptureText(string fileName, IList<string>? arguments = null, TimeSpan? timeout = null)
public static Task<ProcessTextOutput> RunAndCaptureTextAsync(string fileName, IList<string>? arguments = null, CancellationToken cancellationToken = default)
public static ProcessTextOutput RunAndCaptureText(ProcessStartInfo startInfo, TimeSpan? timeout = null)
public static Task<ProcessTextOutput> RunAndCaptureTextAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
```

#### `Process.StartAndForget`
Launches a process and immediately releases the system handle resources, returning only the process ID (PID).
```csharp
public static int StartAndForget(string fileName, IList<string>? arguments = null)
public static int StartAndForget(ProcessStartInfo startInfo)
```

### Reliable Output Reading APIs (Instance Methods)
These methods are called on a `Process` instance to directly read stdout and stderr, guaranteeing no OS pipe buffer overflow deadlocks.

```csharp
public (string StandardOutput, string StandardError) ReadAllText(TimeSpan? timeout = null)
public Task<(string StandardOutput, string StandardError)> ReadAllTextAsync(CancellationToken cancellationToken = default)
public (byte[] StandardOutput, byte[] StandardError) ReadAllBytes(TimeSpan? timeout = null)
public Task<(byte[] StandardOutput, byte[] StandardError)> ReadAllBytesAsync(CancellationToken cancellationToken = default)
public IEnumerable<ProcessOutputLine> ReadAllLines(TimeSpan? timeout = null)
public IAsyncEnumerable<ProcessOutputLine> ReadAllLinesAsync(CancellationToken cancellationToken = default)
```
*Note: `ProcessOutputLine` is a readonly struct containing `string Content` and `bool StandardError` properties.*

### ProcessStartInfo Properties

#### `KillOnParentExit`
Ensures that the spawned child process is terminated when the current (parent) process exits. Works across both Windows and Unix platforms.
```csharp
public bool KillOnParentExit { get; set; }
```

#### `InheritedHandles`
Provides precise control over which file/kernel handles are inherited by the child process, preventing accidental resource leaks.
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

Run a process and safely read all output text without stream deadlock risks:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;

// Run 'git status' and capture output (arguments passed as list)
ProcessTextOutput result = await Process.RunAndCaptureTextAsync("git", ["status"]);

if (result.ExitStatus.ExitCode == 0)
{
    Console.WriteLine($"Git Output: {result.StandardOutput.Trim()}");
}
else
{
    Console.WriteLine($"Failed with exit code: {result.ExitStatus.ExitCode}");
    Console.WriteLine($"Error: {result.StandardError}");
}
```

### 2. Auto-Killing Child Processes on Parent Exit

Ensure a long-running background worker process is killed when the main application terminates:

```csharp
using System.Diagnostics;

var startInfo = new ProcessStartInfo("dotnet", ["run", "--project", "BackgroundWorker.csproj"])
{
    KillOnParentExit = true // Auto-teardown when this parent process exits
};

using var process = Process.Start(startInfo);
// The background worker is now tied to this process's lifecycle
```

### 3. Read All Lines From Output

Start a process and read its output lines safely:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;

var startInfo = new ProcessStartInfo("ping", ["127.0.0.1"])
{
    RedirectStandardOutput = true,
    RedirectStandardError = true
};

using var process = Process.Start(startInfo);
if (process != null)
{
    // Read all output lines safely and asynchronously
    await foreach (ProcessOutputLine line in process.ReadAllLinesAsync())
    {
        string prefix = line.StandardError ? "[Err]" : "[Out]";
        Console.WriteLine($"{prefix} > {line.Content}");
    }
}
```

### 4. Start and Forget (Fire & Forget)

Launch a helper tool or browser without holding onto system handle structures:

```csharp
using System;
using System.Diagnostics;

// Fire and forget, getting back only the process ID
int pid = Process.StartAndForget("notepad.exe");
Console.WriteLine($"Notepad started with PID: {pid}");
```
