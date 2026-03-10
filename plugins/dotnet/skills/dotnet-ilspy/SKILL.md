---
name: dotnet-ilspy
description: >
  Inspect managed .NET assemblies with ILSpy and ilspycmd to understand how APIs work internally.
  Use when the user wants to inspect framework implementation details, reverse engineer a NuGet
  package, compare decompiled C# with IL, or understand behavior in a compiled .NET binary
  without source. DO NOT USE FOR: native binaries, crash dump or profiler analysis, or cases
  where the original source repository or Source Link already provides the needed answer.
---

# .NET Assembly Decompilation with ILSpy

Use this skill to answer "how does this .NET code actually work?" by decompiling the shipped assembly instead of guessing from public API docs. Prefer the smallest relevant binary and the narrowest decompilation scope that answers the user's question.

## When to Use This Skill

- The user wants to understand the internal implementation of a .NET API or type
- The user needs to inspect a NuGet package without cloning its source repository
- The user wants to compare decompiled C# with IL to understand compiler lowering or runtime behavior
- The user has a compiled .NET assembly and wants to see what code shipped
- The user wants to inspect framework code from `Microsoft.NETCore.App` or `Microsoft.AspNetCore.App`

## When Not to Use

- The target is a native binary, a dump, or a runtime performance problem rather than managed code structure
- The original source repository, Source Link, or symbol server already gives a clearer answer
- The user only needs public API usage guidance rather than implementation details
- The target is NativeAOT output or another format that is no longer a normal managed assembly

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Assembly path, package name, or framework component | Yes | The binary to inspect, or enough information to locate it |
| Type, member, or behavior to investigate | Yes | The exact API, class, method, or question the user wants explained |
| Target framework or package version | Recommended | Needed to choose the correct `lib/`, `runtimes/`, or shared framework assembly |
| ILSpy CLI availability | Recommended | Whether `dnx ilspycmd` or `ilspycmd` is already available on the machine |

## Workflow

### Step 1: Confirm that the target is the right managed assembly

Start by identifying where the implementation actually lives.

- For NuGet packages, prefer `lib/<tfm>/` or `runtimes/<rid>/lib/<tfm>/`
- Do not start with `ref/<tfm>/` when the user wants implementation details; reference assemblies usually omit method bodies
- For shared framework code, run `dotnet --list-runtimes` and inspect the matching directory under `shared/Microsoft.NETCore.App/` or `shared/Microsoft.AspNetCore.App/`
- For app code, inspect `bin/Debug/<tfm>/`, `bin/Release/<tfm>/`, or published output

If the user only knows the package or framework name, use [references/common-assembly-locations.md](references/common-assembly-locations.md) to map it to a likely assembly path.

### Step 2: Use the best ILSpy CLI entry point available

Prefer a reproducible CLI flow over the GUI when answering from a terminal session.

```bash
dnx ilspycmd -h
```

If `dnx` is unavailable or the environment already has ILSpy installed as a tool, use:

```bash
ilspycmd -h
```

If neither command works and installing a tool is acceptable in the current environment, install the CLI tool:

```bash
dotnet tool install --global ilspycmd
```

If installation is not appropriate, stop and tell the user what is missing instead of inventing output. The other fallback is the ILSpy desktop application.

### Step 3: Find the exact type before decompiling everything

List types first so you can target the correct namespace and avoid noisy output.

```bash
dnx ilspycmd -l class "path/to/Assembly.dll"
dnx ilspycmd -l interface "path/to/Assembly.dll"
```

Use the narrowest list that fits the question. If the expected type is missing, check whether:

- You chose the wrong target framework or package asset
- The type was forwarded into another assembly
- The implementation lives in a helper assembly rather than the public facade

### Step 4: Decompile the smallest useful scope

For a focused answer, start with a single type:

```bash
dnx ilspycmd -t Namespace.TypeName "path/to/Assembly.dll"
```

For broader exploration:

```bash
dnx ilspycmd -o ./decompiled "path/to/Assembly.dll"
dnx ilspycmd -p -o ./decompiled-project "path/to/Assembly.dll"
```

Use `-p` only when the user needs to browse multiple files or understand relationships across many types.

### Step 5: Switch to IL when the decompiled C# hides important details

Decompiled C# is often good enough for control flow, but IL is better when you need exact lowering details.

```bash
dnx ilspycmd -il -t Namespace.TypeName "path/to/Assembly.dll"
```

Use IL when investigating:

- Async and iterator state machines
- Boxing, constrained calls, and interface dispatch
- Pattern matching or switch lowering
- `unsafe`, `ref struct`, or span-heavy code
- Differences between what the author wrote and what the compiler emitted

### Step 5a: Use concrete command patterns for common investigations

Framework implementation:

```bash
dotnet --list-runtimes
dnx ilspycmd -l class "C:/Program Files/dotnet/shared/Microsoft.NETCore.App/8.0.0/System.Text.Json.dll"
dnx ilspycmd -t System.Text.Json.JsonSerializer "C:/Program Files/dotnet/shared/Microsoft.NETCore.App/8.0.0/System.Text.Json.dll"
```

NuGet package source inspection:

```bash
dnx ilspycmd -t Polly.Retry.RetryPolicy "~/.nuget/packages/polly/8.0.0/lib/net8.0/Polly.dll"
dnx ilspycmd -p -o ./polly-src "~/.nuget/packages/polly/8.0.0/lib/net8.0/Polly.dll"
```

Compare reconstructed C# with IL for the same type:

```bash
dnx ilspycmd -t Namespace.TypeName "path/to/Assembly.dll"
dnx ilspycmd -il -t Namespace.TypeName "path/to/Assembly.dll"
```

### Step 6: Follow the implementation, not just the public entry point

Many framework and package APIs are thin wrappers. If the first method you inspect only delegates elsewhere:

1. Identify the internal helper type or nested generated state machine it calls
2. Decompile that helper type next
3. Keep track of the assembly version and target framework while you follow the chain

When the user asks about framework behavior, mention the exact runtime version you inspected because implementations can differ between releases.

### Step 7: Report findings with provenance and caveats

When answering the user:

- Name the assembly path, package version, or runtime version you inspected
- Call out the exact type or member that explains the behavior
- Mention when the answer comes from IL rather than decompiled C#
- Treat decompiled source as reconstructed code, not the exact original source file

If the result is still ambiguous and Source Link or the upstream repository is available, recommend checking the original source as a confirmation step.

## Validation

- [ ] The inspected binary is an implementation assembly, not just a `ref/` facade, when internals matter
- [ ] The decompilation command targets the specific type or assembly relevant to the user's question
- [ ] The answer identifies the assembly, version or target framework, and the type or member inspected
- [ ] The final explanation notes any important decompilation caveats or version-specific behavior

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Decompiling `ref/<tfm>/` and finding empty or stubbed method bodies | Switch to `lib/<tfm>/`, `runtimes/<rid>/lib/<tfm>/`, or the shared runtime assembly |
| Inspecting the wrong NuGet asset for the app's target framework | Choose the nearest matching TFM and prefer runtime-specific assets when behavior differs by platform |
| Assuming the first public method contains the real logic | Follow delegated calls into helper types, internal implementations, or generated state machines |
| Treating decompiled C# as the exact original source | Explain that ILSpy reconstructs readable C# and that names, formatting, and some constructs may differ |
| Using ILSpy for native, obfuscated, or NativeAOT binaries | Switch to native debugging or binary analysis tooling; this skill is for managed assemblies |

## Reference Files

- **[references/common-assembly-locations.md](references/common-assembly-locations.md)** - Common places to find runtime, NuGet, SDK, and build output assemblies. Load when the user knows the package or framework but not the binary path.

## References

- [ILSpy repository](https://github.com/icsharpcode/ILSpy)
- `dnx ilspycmd -h` or `ilspycmd -h` for the local CLI help text
