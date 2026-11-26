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
