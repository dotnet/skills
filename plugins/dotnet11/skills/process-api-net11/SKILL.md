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

## Capturing Output Without Pipe Buffer Deadlocks

Redirecting standard output and standard error creates two separate pipes. Each pipe has a finite buffer. If the child fills a pipe, its next write blocks until the parent reads data from that pipe.

### Do Not Wait for Exit Before Reading

With both streams redirected, this code can deadlock:

```csharp
ProcessStartInfo startInfo = new("dotnet", ["--help"])
{
    RedirectStandardOutput = true,
    RedirectStandardError = true
};

using Process process = new() { StartInfo = startInfo };
process.Start();
process.WaitForExit();

string output = process.StandardOutput.ReadToEnd();
string error = process.StandardError.ReadToEnd();
```

The parent waits for the child to exit without draining either pipe. If the child fills either buffer, it blocks waiting for the parent to read, so neither process can make progress. Small outputs may hide this bug; it is not safe to assume the output will fit in the buffers.

### Do Not Read Standard Output and Standard Error Sequentially

Moving the reads before the wait does not fix the problem:

```csharp
process.Start();

string output = process.StandardOutput.ReadToEnd();
string error = process.StandardError.ReadToEnd();

process.WaitForExit();
```

Here the parent reads stdout until EOF before it starts draining stderr. If the child fills stderr while stdout remains open, the child blocks writing stderr and the parent blocks waiting for stdout to close. Replacing these reads with sequentially awaited `ReadToEndAsync` calls has the same problem.

**Start draining both redirected streams concurrently while the child runs; never wait for process exit without draining them.** The following .NET 11 APIs do this for you. They prevent this pipe-buffer deadlock, not every possible hang: a descendant that retains an inherited pipe writer can still delay EOF.

### Capture Text or Bytes From a Started Process

Use `Process.ReadAllText` or `Process.ReadAllTextAsync` to capture both streams as text. They drain stdout and stderr concurrently and return a tuple containing both strings. Text is decoded using `ProcessStartInfo.StandardOutputEncoding` and `StandardErrorEncoding`, or the defaults when unspecified.

Both text and byte readers require `RedirectStandardOutput = true` and `RedirectStandardError = true` before starting the process:

```csharp
public (string StandardOutput, string StandardError) ReadAllText(TimeSpan? timeout = null)
public Task<(string StandardOutput, string StandardError)> ReadAllTextAsync(CancellationToken cancellationToken = default)
public (byte[] StandardOutput, byte[] StandardError) ReadAllBytes(TimeSpan? timeout = null)
public Task<(byte[] StandardOutput, byte[] StandardError)> ReadAllBytesAsync(CancellationToken cancellationToken = default)
```

**Use full-output capture only when the output size is trusted and bounded.** `ReadAllText[Async]` and `ReadAllBytes[Async]` read until EOF and buffer all captured output in the parent process's memory, without an output-size limit. An untrusted child can produce unlimited output and exhaust the parent's memory, causing an out-of-memory failure. A timeout or cancellation token is not a memory limit.

For untrusted output, drain both streams concurrently with fixed-size byte buffers and enforce one shared retention budget across stdout and stderr before retaining each chunk. On overflow, either terminate the child and wait for termination, or keep only the bounded prefix and continue draining and discarding excess bytes. Report truncation or the limit violation. Do not simply stop reading a still-running child: it can block on full pipes. Give execution and output draining separate lifetime policies because descendants can keep writers open after the immediate child exits.

```csharp
ProcessStartInfo startInfo = new("dotnet", ["--help"])
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    InheritedHandles = []
};

using Process process = new() { StartInfo = startInfo };
process.Start();

(string output, string error) = process.ReadAllText();
ProcessExitStatus status = process.WaitForExitStatus();
```

For asynchronous reading, replace the last two lines with:

```csharp
(string output, string error) = await process.ReadAllTextAsync();
ProcessExitStatus status = await process.WaitForExitStatusAsync();
```

Use `Process.ReadAllBytes` or `Process.ReadAllBytesAsync` instead when the output is binary or you need the original bytes without text decoding. With the same redirection setup, replace the last two lines of the synchronous example with:

```csharp
(byte[] output, byte[] error) = process.ReadAllBytes();
ProcessExitStatus status = process.WaitForExitStatus();
```

The asynchronous equivalent is:

```csharp
(byte[] output, byte[] error) = await process.ReadAllBytesAsync();
ProcessExitStatus status = await process.WaitForExitStatusAsync();
```

These instance methods read to EOF; they do not replace waiting for process exit. A child can close its standard streams and keep running. After reading, use `WaitForExitStatus` or await `WaitForExitStatusAsync` to wait for termination and obtain the exit status. See [Waiting for Process Exit](#waiting-for-process-exit) for cancellation and timeout policies.

### Process Output Line by Line With ReadAllLines[Async]

Use `Process.ReadAllLines` or `Process.ReadAllLinesAsync` to read stdout and stderr as one sequence of `ProcessOutputLine` values, interleaved as lines become available. `ProcessOutputLine` is a readonly struct with read-only properties `string Content` for the line's text and `bool StandardError` identifying its source. Enable both output and error redirection before starting the process. Consume the sequence with `foreach` or `await foreach` while the process runs; merely obtaining the enumerable does not drain output.

```csharp
public IEnumerable<ProcessOutputLine> ReadAllLines(TimeSpan? timeout = null)
public IAsyncEnumerable<ProcessOutputLine> ReadAllLinesAsync(CancellationToken cancellationToken = default)
```

Prefer these methods when the interleaving of output and error matters. Capturing stdout and stderr separately and then concatenating the strings loses that interleaving. For example, a child might print:

```text
OUT: A
ERR: B
OUT: C
```

Concatenating captured stdout followed by captured stderr instead produces:

```text
OUT: A
OUT: C
ERR: B
```

The line APIs let you process both streams together instead of grouping all stdout before all stderr. They report the order in which lines become available to the reader, not a guaranteed reconstruction of the child's exact write order across two separate pipes.

Also prefer line-by-line reading when you only need to search for specific text: inspect each line rather than allocating two potentially large strings containing the complete output. With the same process-start and redirection setup as above:

```csharp
bool found = false;
await foreach (ProcessOutputLine line in process.ReadAllLinesAsync())
{
    if (line.Content.Contains("needle", StringComparison.Ordinal))
    {
        found = true;
    }
}
ProcessExitStatus status = await process.WaitForExitStatusAsync();
```

After finding a match, keep draining if the child should run to completion. If it is long-running and no longer needed, kill it and wait for termination instead. Do not simply stop enumeration and wait for a still-running child: it may block on a full pipe.

Line-by-line reading avoids retaining the entire output, but it is not a strict memory bound for untrusted output: a single arbitrarily long line can still exhaust memory.

### Start, Capture Text, and Wait for Exit in One Call

When you only need to start a process, capture its text output and error, and wait for its exit status, prefer `Process.RunAndCaptureText`:

```csharp
ProcessTextOutput result = Process.RunAndCaptureText("dotnet", ["--help"]);
```

Use `Process.RunAndCaptureTextAsync` when asynchronous execution is needed:

```csharp
ProcessTextOutput result = await Process.RunAndCaptureTextAsync("dotnet", ["--help"]);
```

Both methods combine starting the process, draining both streams, and waiting for exit. They return the framework's sealed `ProcessTextOutput` class, with read-only properties `ProcessExitStatus ExitStatus`, `string StandardOutput`, `string StandardError`, and `int ProcessId`. No separate `Process` instance or wait is needed. They also accept `ProcessStartInfo` for custom configuration, so needing configuration alone is not a reason to use the instance readers. For those overloads, enable both `RedirectStandardOutput` and `RedirectStandardError` yourself; the methods do not modify the supplied configuration.

The synchronous overloads accept a timeout; the asynchronous overloads accept a cancellation token. If timeout or cancellation interrupts output capture, the helper attempts to kill the process and rethrows `TimeoutException` or `OperationCanceledException`, respectively. If capture has completed and the subsequent exit wait kills the process, the helper returns a result with `result.ExitStatus.Canceled` set to `true`. Do not assume timeout or cancellation always produces a returned result.

```csharp
public static ProcessTextOutput RunAndCaptureText(string fileName, IEnumerable<string>? arguments = null, TimeSpan? timeout = null)
public static Task<ProcessTextOutput> RunAndCaptureTextAsync(string fileName, IEnumerable<string>? arguments = null, CancellationToken cancellationToken = default)
public static ProcessTextOutput RunAndCaptureText(ProcessStartInfo startInfo, TimeSpan? timeout = null)
public static Task<ProcessTextOutput> RunAndCaptureTextAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
```

`RunAndCaptureText[Async]` also buffers all captured output in memory without an output-size limit. The same trusted, bounded-output requirement applies; do not use it to capture potentially unlimited output from an untrusted process.

### Run and Wait Without Capturing Output

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

## Redirecting Standard Handles

Use `ProcessStartInfo.StandardInputHandle`, `StandardOutputHandle`, and `StandardErrorHandle` to connect a child's standard streams directly to `SafeFileHandle` instances. This supports process-to-process piping, writing output directly to a file, providing file input, discarding output, and breaking inheritance of the parent's terminal or pipe handles. Unlike `RedirectStandardOutput = true`, assigning a handle does not create a stream for the parent to read through `process.StandardOutput`.

**When output only needs to go to a file or another process, redirect handles directly instead of capturing it in the parent and writing it out again.** Direct file-handle redirection or process-to-process piping is the most efficient approach for these uses: it avoids unnecessary parent-side buffering, copying, and text decoding/re-encoding. Only read output into the parent when it needs to inspect or transform the data.

Leave the corresponding `RedirectStandardInput`, `RedirectStandardOutput`, or `RedirectStandardError` flag set to `false`; setting both a handle and its redirection flag throws.

| Destination or source | API |
|-----------------------|-----|
| Connected pipe ends for process piping | `SafeFileHandle.CreateAnonymousPipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe)` |
| File input or output | `File.OpenHandle(...)` |
| EOF on input or discarded output/error | `File.OpenNullHandle()` |
| Explicit access to the parent's standard handles | `Console.OpenStandardInputHandle()`, `Console.OpenStandardOutputHandle()`, `Console.OpenStandardErrorHandle()` |

`CreateAnonymousPipe` also accepts `asyncRead` and `asyncWrite` flags for advanced scenarios. Only enable asynchronous handles when the code using that end supports them; child programs normally expect synchronous standard handles.

### Pipe One Process Into Another and Write to a File

On Unix with `ls` and `grep` installed, this is equivalent to:

```sh
ls /usr/bin | grep zip > output.txt
```

The processes exchange bytes through the pipe directly, without the parent reading, buffering, or copying the output:

```csharp
using System.Diagnostics;
using System.IO;
using Microsoft.Win32.SafeHandles;

SafeFileHandle.CreateAnonymousPipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe);

using (readPipe)
using (writePipe)
using (SafeFileHandle outputFile = File.OpenHandle("output.txt", FileMode.Create, FileAccess.Write))
{
    ProcessStartInfo producer = new("ls", ["/usr/bin"])
    {
        StandardOutputHandle = writePipe,
        InheritedHandles = []
    };

    ProcessStartInfo consumer = new("grep", ["zip"])
    {
        StandardInputHandle = readPipe,
        StandardOutputHandle = outputFile,
        InheritedHandles = []
    };

    using Process producerProcess = Process.Start(producer)!;
    writePipe.Dispose();

    using Process consumerProcess = Process.Start(consumer)!;
    readPipe.Dispose();
    outputFile.Dispose();

    ProcessExitStatus producerStatus = await producerProcess.WaitForExitStatusAsync();
    ProcessExitStatus consumerStatus = await consumerProcess.WaitForExitStatusAsync();
}
```

Start both processes before waiting for either to exit so the consumer can drain the producer's pipe. After each successful start, the child owns its copy of the supplied handles; dispose the parent's copies when no longer needed. In particular, retaining the parent's write end would prevent the consumer from reaching EOF even after the producer exits. Disposing these parent handles does not close the children's copies.

The handle-creation helpers create non-inheritable handles. **Do not manually enable inheritance**: `Process` handles passing the specified standard handles to the child. This avoids unnecessarily exposing them to other processes started concurrently.

## Controlling Handle Inheritance

**A pipe reaches EOF only when all handles to its write end have been closed.** Reading both output streams concurrently prevents pipe-buffer deadlocks, but it cannot force EOF while another process still owns a writer. There are two distinct inheritance problems to consider.

### Accidental Inheritance by a Sibling Process

If two threads start children concurrently, an inheritable handle intended for one child can leak into the other:

```log
Parent Process
|- Thread A                          |- Thread B
|                                    |
|  1. Create an inheritable pipe     |  1. Create no pipes, just inherit parent standard handles
|    (read_A, write_A)               |
|                                    |
|  2. Start Child A                  |  2. Start Child B
|     inherits: the pipe             |     inherits: parent standard handles, read_A and write_A (accidental!)
|                                    |
|  3. Close write_A (parent copy)    |  3. Keeps running and holds inherited write_A open
|                                    |
|  4. Child A exits                  |
|                                    |
|  5. Read from read_A               |
|     ┌──────────────────────┐       |
|     │  HANGS FOREVER!      │       |
|     │  write_A is still    │       |
|     │  open in Child B     │       |
|     └──────────────────────┘       |
```

Child B need not write anything to the pipe to keep it open. If it is long-running, the parent's read can remain pending indefinitely after Child A has exited.

.NET creates file handles as non-inheritable by default and coordinates process creation with a global lock on Windows to prevent its temporary inheritable handles from leaking between cooperating starts. Do not undermine these protections by creating inheritable handles yourself: avoid `FileShare.Inheritable`, `HandleInheritability.Inheritable`, Windows P/Invoke handles with `bInheritHandle = true`, or Unix descriptors without `FD_CLOEXEC` unless inheritance is explicitly required and coordinated. Native process-start code does not automatically participate in .NET's lock.

#### Custom P/Invoke Process Creation

**When custom P/Invokes are needed to spawn a process, you must use `WindowsProcessStartArguments.Start` or `UnixProcessStartArguments.Start` rather than bypassing .NET's process-start coordination.** Both types are in `System.Runtime.InteropServices`. They prepare standard handles, arguments, and environment data, then invoke your native process-creation callback while holding the runtime's process-start lock.

| Platform | API returning a `Process` | Callback result |
|----------|---------------------------|-----------------|
| Windows | `WindowsProcessStartArguments.Start(ProcessStartInfo startInfo, Func<WindowsProcessStartArguments, nint> callback)` | Valid native process handle; ownership transfers to the returned `Process` |
| Unix | `UnixProcessStartArguments.Start(ProcessStartInfo startInfo, Func<UnixProcessStartArguments, int> callback)` | Positive PID of the newly created child |

On Windows, the callback runs under the global exclusive writer lock, coordinating with .NET process starts so it does not accidentally inherit their temporary inheritable handles or expose its prepared standard handles to them. On Unix, the callback runs under the runtime's shared reader lock, coordinating child creation and exit tracking. Do not try to acquire these internal locks yourself.

Use the callback's prepared data in the platform-specific native call. These entry points prepare standard handles, arguments, and environment data; they do not automatically apply all `ProcessStartInfo` launch policies. Inspect the callback argument's `ProcessStartInfo` and implement requested settings such as `WorkingDirectory`, `StartDetached`, and `KillOnParentExit` in the native launch, or reject unsupported settings before creating the child. Never silently launch without a requested working directory or parent-exit protection. Prefer ordinary `Process.Start` when you cannot preserve those policies in the callback.

- On Windows, use the runtime-prepared `StandardInput`, `StandardOutput`, and `StandardError` values, derived from `ProcessStartInfo`, in the native startup structure; do not create or choose replacement handles. Use the prepared `Arguments` and `EnvironmentVariables` for the command line and environment. The environment block is UTF-16; configure the chosen native process-creation API to interpret it as Unicode (for APIs with a creation-flags parameter, set `CREATE_UNICODE_ENVIRONMENT`). Close any native thread handle that the creation API returns; return the process handle without closing it.
- On Unix, use the runtime-prepared standard descriptors and `ResolvedPath`, `Arguments`, and `EnvironmentVariables` with the chosen native process-creation mechanism. For a custom `fork`/`execve` path, map the prepared descriptors to stdin, stdout, and stderr in the child, perform the required native setup, then execute the target. Keep post-fork, pre-exec logic in native code and follow async-signal-safety requirements; do not run managed callbacks in the forked child. Check native errors and report failures to the parent callback, which must throw rather than return an invalid PID.

The returned `Process` supports the same output-reading and status-wait APIs described above. See [dotnet/runtime#128862](https://github.com/dotnet/runtime/pull/128862) for Windows `CreateProcess` and Unix `posix_spawn` examples and their P/Invoke declarations.

**Create the child synchronously inside the callback.** Pointer-backed arguments are valid only during that invocation; do not cache them or defer creation to another thread. Do not call other .NET process-start APIs from inside the callback: the lock is already held, so reentrant starts can deadlock or throw.

The callback remains responsible for applying the chosen native API's inheritance controls correctly; holding the runtime lock does not itself restrict which handles the child inherits. Keep unrelated handles non-inheritable, and do not manually enable inheritance on the prepared standard handles or descriptors.

### Restrict Inheritance With ProcessStartInfo.InheritedHandles

Use `InheritedHandles` to specify which additional handles a child may inherit, instead of exposing all inheritable handles in the parent:

```csharp
public IList<SafeHandle>? InheritedHandles { get; set; }
```

**When configuring `ProcessStartInfo`, enable restricted inheritance on every platform**: use `InheritedHandles = []` when no additional handles are needed, or an explicit list when they are.

The filename overloads of `Run[Async]`, `RunAndCaptureText[Async]`, and `StartAndForget` are convenient, but retain the default inheritance policy. Use their `ProcessStartInfo` overloads when restricted inheritance is required. The short filename-overload examples in this skill demonstrate each operation, not a restricted-inheritance configuration.

| Value | Child inherits |
|-------|----------------|
| `null` (default) | Standard handles plus other inheritable handles, preserving the existing behavior |
| `[]` | Only standard input, output, and error handles |
| Explicit non-empty list | Standard handles plus the listed handles |

For a child that needs no additional handles:

```csharp
ProcessStartInfo startInfo = new("dotnet", ["build"])
{
    InheritedHandles = []
};

using Process process = Process.Start(startInfo)!;
ProcessExitStatus status = await process.WaitForExitStatusAsync();
```

Apply the restriction to each relevant child start: restricting Child A does not prevent an unrestricted Child B from inheriting other handles. Standard handles are included automatically; do not add them to the list. Only `SafeFileHandle` and `SafePipeHandle` instances are supported, and a non-null list requires an empty `UserName`.

**Do not manually enable inheritance on handles passed in this list.** The runtime manages their inheritance flags during process creation. Do not pass the same handle in concurrent process starts; serialize starts sharing that handle or supply separate handles.

On Windows, explicit lists allow concurrent starts under a shared reader lock rather than serializing them under the default exclusive writer lock.

#### Unix Support and Older Linux Compatibility

All officially supported .NET 11 Unix environments that support process creation provide the required native facilities:

- Supported Apple platforms provide `POSIX_SPAWN_CLOEXEC_DEFAULT`.
- Illumos/Solaris provides `fdwalk`.
- FreeBSD provides `close_range`.
- Supported Linux distributions provide `close_range` with `CLOSE_RANGE_CLOEXEC`, either through a sufficiently recent kernel or a distribution backport.

Very old, unsupported Linux environments may otherwise happen to run .NET applications but lack these facilities. On those systems, starting a process with a non-null `InheritedHandles` list throws `PlatformNotSupportedException`. Do not disable restricted inheritance on supported distributions merely because their nominal kernel version is old: enterprise distributions such as RHEL 8 backport the required functionality.

Only for libraries that deliberately preserve compatibility with unsupported older Linux environments, use this conservative version check:

```csharp
processStartInfo.InheritedHandles = !OperatingSystem.IsLinux() || Environment.OSVersion.Version >= new Version(5, 11) ? [] : null;
```

Upstream Linux added `CLOSE_RANGE_CLOEXEC` in 5.11. This check is a compatibility heuristic, not a capability probe: it also disables restricted inheritance on older-versioned kernels with working backports. Setting `null` restores unrestricted inheritance of inheritable handles, so use this compatibility policy only when that tradeoff is acceptable; do not fall back when restricted inheritance is a correctness or security requirement.

### Inheritance by a Grandchild Process

Even with the parent's inheritance restricted, the child receives its standard handles and can pass them to its own children:

```log
Parent Process                 Child Process                Grandchild Process
|                              |                            |
|  1. Create pipe              |                            |
|     (read, write)            |                            |
|                              |                            |
|  2. Start Child              |                            |
|     inherits: write          |                            |
|                              |                            |
|  3. Close write              |  1. Start Grandchild       |
|     (parent copy)            |     inherits: write        |
|                              |                            |
|  4. Read from read...        |  2. Child exits            |
|     ┌──────────────────┐     |     closes write           |
|     │  HANGS!          │     |                            |  Still running
|     │  write is still  │     |                            |  keeps write open
|     │  open in         │     |                            |
|     │  Grandchild      │     |                            |
|     └──────────────────┘     |                            |
```

**`InheritedHandles = []` does not prevent a child from passing its standard handles to a grandchild.** The parent cannot control how an arbitrary child launches descendants. When you control that code, consider whether the grandchild should inherit the child's output pipe or use separate standard handles, such as a new pipe, a file, or the null device. Choose based on the intended output flow and lifetime; retaining the inherited writer can delay EOF in the parent.

For an arbitrary child, use `WaitForExitStatus[Async]` to observe its termination independently of output EOF. If complete output capture can remain pending on descendant-held writers, define an explicit output-draining cancellation or timeout policy. This can be achieved by consuming `ReadAllLinesAsync` with a cancellation token and canceling that token when a concurrent `WaitForExitStatusAsync` completes. Do not assume killing or waiting for the immediate child closes every writer. Stopping reads before EOF can lose remaining output, so make that tradeoff explicit.

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

On Unix, `Signal(PosixSignal.SIGTERM)` can request graceful shutdown; the child may handle or ignore it. Follow it with `TryWaitForExit` for a bounded grace period, then `Kill` and a wait if forceful termination is required. `PosixSignal` is in `System.Runtime.InteropServices`. `Signal` returns `false` if the process has already exited or does not exist.

Dispose handles returned by `Start`, normally with `using`; disposal releases parent-side resources, not the child process. If you already have a `Process`, its `SafeHandle` property exposes these operations without starting another process. That handle belongs to the `Process`; do not dispose it independently while continuing to use the wrapper. For custom native process creation, use the coordinated entry points described in [Custom P/Invoke Process Creation](#custom-pinvoke-process-creation).
