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

These instance methods read to EOF; they do not replace waiting for process exit. A child can close its standard streams and keep running. After reading, use `WaitForExitStatus` or await `WaitForExitStatusAsync` to wait for termination and obtain the exit status. See [Waiting for Process Exit](waiting-and-termination.md#waiting-for-process-exit) for cancellation and timeout policies.

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
