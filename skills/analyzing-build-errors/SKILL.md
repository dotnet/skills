---
name: analyzing-build-errors
description: Triage and resolve .NET build failures including compilation errors, NuGet restore issues, SDK version mismatches, and MSBuild target errors. Use when dotnet build fails, NuGet restore produces errors, or the user encounters red squiggles and build diagnostics.
---

# Analyzing Build Errors

## When to Use

- `dotnet build` fails with compilation errors
- `dotnet restore` fails with NuGet resolution errors
- MSBuild produces warnings or errors about targets, props, or SDK versions
- The user reports red squiggles in their IDE that block compilation

## When Not to Use

- The issue is a runtime exception (not a build error)
- The user wants to improve code quality without fixing build breaks
- The errors are from a non-.NET build system

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.sln` or `.csproj` that fails to build |
| Error output | No | The raw error text; if not provided, run `dotnet build` to capture it |

## Workflow

### Step 1: Reproduce the failure

```bash
dotnet build <project-or-solution> 2>&1
```

Capture the full output. Errors follow the format:

```
<file>(<line>,<col>): error <CODE>: <message>
```

### Step 2: Categorize the error

| Error Code Pattern | Category | Go To |
|---|---|---|
| `CS####` | C# compiler error | [Compiler errors](#compiler-errors) |
| `NU####` | NuGet error | [NuGet errors](#nuget-errors) |
| `MSB####` | MSBuild error | [MSBuild errors](#msbuild-errors) |
| `NETSDK####` | .NET SDK error | [SDK errors](#sdk-errors) |

### Compiler errors

The most common `CS` errors and their fixes:

| Error | Meaning | Fix |
|---|---|---|
| `CS0246` | Type or namespace not found | Add missing `using` directive or NuGet package reference |
| `CS1061` | Member not found on type | Check for typos, missing interface implementation, or wrong type |
| `CS0103` | Name does not exist in scope | Declare the variable, or fix the scope |
| `CS8600`-`CS8605` | Nullable reference warnings-as-errors | Add null checks, use `!`, or adjust nullable annotations |
| `CS0619` | Obsolete API used as error | Replace with the recommended API from the error message |
| `CS0029` | Cannot implicitly convert type | Add explicit cast or fix the type mismatch |

For other `CS` errors, use the error code to look up documentation:

```
https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs<NUMBER>
```

### NuGet errors

| Error | Meaning | Fix |
|---|---|---|
| `NU1100` | Unable to resolve package | Check package name spelling, NuGet source configuration |
| `NU1102` | Package version not found | Verify version exists; try `dotnet nuget list source` |
| `NU1202` | Package not compatible with target framework | Find a compatible version or an alternative package |
| `NU1605` | Package downgrade detected | Align versions across projects or use central package management |
| `NU1903` | Known vulnerability in package | Update to patched version listed in the warning |

Restore diagnostics:

```bash
dotnet restore --verbosity detailed
```

### MSBuild errors

| Error | Meaning | Fix |
|---|---|---|
| `MSB3644` | Reference assemblies not found | Install the targeting pack for the framework version |
| `MSB4019` | Imported project not found | Verify SDK installation, check `global.json` |
| `MSB3270` | Processor architecture mismatch | Align `PlatformTarget` in project files |

### SDK errors

| Error | Meaning | Fix |
|---|---|---|
| `NETSDK1045` | SDK version too old | Update SDK or adjust `global.json` |
| `NETSDK1004` | Assets file not found | Run `dotnet restore` first |
| `NETSDK1005` | Assets file has wrong target | Delete `obj/` folder and restore again |
| `NETSDK1141` | SDK version in `global.json` not found | Install matching SDK or remove the `global.json` constraint |

### Step 3: Apply the fix

Make the specific code or project file change. Then rebuild:

```bash
dotnet build <project-or-solution>
```

### Step 4: Verify the fix

- [ ] Build succeeds with zero errors
- [ ] No new warnings introduced (or warnings are acknowledged)
- [ ] `dotnet test` still passes (if tests exist)

## Triage Shortcut: Clean Rebuild

When the error seems stale or inconsistent with the code:

```bash
dotnet clean
dotnet nuget locals all --clear
dotnet restore
dotnet build
```

This resolves most phantom build errors caused by stale caches.

## Validation

- [ ] `dotnet build` produces zero errors
- [ ] Root cause of each error is identified
- [ ] Fix is minimal and targeted (no unrelated changes)
- [ ] No new warnings elevated to errors

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Fixing symptoms instead of root cause | Read the full error message; the root error is often the last one |
| Build order issues in solutions | Check `ProjectReference` dependencies; build the dependency first |
| `global.json` pinning a missing SDK | Remove or update the `global.json` file |
| NuGet source authentication failures | Run `dotnet nuget list source` and verify credentials |
| Incremental build inconsistencies | Use `dotnet clean` followed by full rebuild |
