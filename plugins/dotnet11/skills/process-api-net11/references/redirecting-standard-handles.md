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
