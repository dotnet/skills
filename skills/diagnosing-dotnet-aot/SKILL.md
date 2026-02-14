---
name: diagnosing-dotnet-aot
description: >-
  Diagnoses .NET Native AOT and trimming compatibility issues and provides
  concrete fixes. Activates when preparing an application or library for
  PublishAot, fixing IL warnings (IL2026, IL3050, IL2070), migrating reflection
  to source generators, or evaluating library AOT compatibility.
---

# Diagnosing .NET AOT Issues

Scan C# code for Native AOT and trimming incompatibilities and produce prioritized fixes. Goal: **zero-warning AOT publish**.

## Why This Skill Exists

LLMs already know general AOT concepts. This skill adds knowledge that base models **consistently get wrong**:

| What Claude Gets Wrong Without This Skill | Correct Answer |
|------------------------------------------|----------------|
| Suggests `#pragma warning disable` for trim warnings | `#pragma` is **not preserved in IL** — trimmer ignores it. Must use `[UnconditionalSuppressMessage]` |
| Flags ALL `MakeGenericType` as dangerous | Safe for **reference types** (shared canonical code). Only value types need pre-generated code |
| Misses `Expression.Compile()` perf cliff | Falls back to **10-100x slower interpreter** in AOT — **no warning emitted** |
| Suggests fixing `EventSource.WriteEvent` IL2026 | False positive for >3 params with **primitive types** — safe to suppress |
| Doesn't know `IsAotCompatible=true` cascades | Automatically enables `IsTrimmable` + `EnableTrimAnalyzer` + `EnableSingleFileAnalyzer` + `EnableAotAnalyzer` |
| Gives incomplete annotation workflow | Must propagate `[DynamicallyAccessedMembers]` through **entire call chain** — leaf → caller → caller → call site |
| Inconsistent on EF Core AOT status | Experimental: requires compiled models + precompiled queries, not production-ready |

## When to Use

- Preparing an app for `<PublishAot>true</PublishAot>` or a library for `<IsAotCompatible>true</IsAotCompatible>`
- Diagnosing IL trimming or AOT warnings (IL2026, IL2070, IL3050, IL3058, IL2104)
- Migrating reflection-heavy code to source generators
- Evaluating dependency AOT compatibility
- Reviewing PRs touching reflection, serialization, DI, or generic type construction

## When Not to Use

- **Runtime performance tuning** — use `dotnet-performance-patterns`
- **Projects that will never use AOT**
- **WPF or Windows Forms** — not AOT-compatible

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Source code | Yes | C# files, project files, or repo paths |
| Target framework | Recommended | .NET version (affects available source generators) |
| Project type | Recommended | App vs library (different annotation strategy) |
| Scan depth | Optional | `critical-only`, `standard` (default), `comprehensive` |

## Workflow

### Step 1: Load Critical Patterns

Always load [critical-patterns.md](references/critical-patterns.md) — hard failures that **will** crash or corrupt data in AOT.

### Step 2: Detect Code Signals and Load Topic References

Scan for signals and load only the relevant reference files:

| Signal in Code | Load Reference |
|----------------|----------------|
| `JsonSerializer`, `Newtonsoft`, `JsonConvert`, `IConfiguration`, `IOptions<`, `Bind(`, `[JsonSerializable` | [serialization-and-config.md](references/serialization-and-config.md) |
| `Type.GetType`, `GetMethod(`, `Activator.CreateInstance`, `Assembly.Load`, `[DynamicallyAccessedMembers`, `IServiceCollection`, DI registration | [reflection-and-di.md](references/reflection-and-di.md) |
| `MakeGenericType`, `MakeGenericMethod`, `Expression.`, `Linq.Expressions`, struct generics | [generics-and-types.md](references/generics-and-types.md) |
| `<PackageReference`, `using` for EF Core / gRPC / SignalR / known libraries | [library-compatibility.md](references/library-compatibility.md) |
| `.csproj`, `Directory.Build.props`, `PublishAot`, `IsAotCompatible`, CI/CD files | [project-setup.md](references/project-setup.md) |

**Scan depth:** `critical-only` = Step 1 only. `standard` (default) = Steps 1+2. `comprehensive` = all files.

### Step 3: Scan Code Against Loaded Patterns

Match on specific API calls, structural patterns, missing source generation, missing annotations, and project configuration gaps.

### Step 4: Classify Findings

| Severity | Criteria | Action |
|----------|----------|--------|
| 🔴 **Critical** | Will crash, throw, or corrupt data in AOT | Must fix |
| 🟡 **Warning** | Produces IL warnings at publish; not guaranteed to work | Should fix |
| ℹ️ **Info** | Sub-optimal (larger binary, slower path) but functional | Consider fixing |

### Step 5: Generate Fix Suggestions

For each finding provide:
1. **What**: The incompatible pattern (one sentence)
2. **Why**: What goes wrong in AOT (crash type or warning code)
3. **Fix**: ❌ current code → ✅ AOT-compatible replacement
4. **Version**: Minimum .NET version required

Group by file, then by severity (🔴 → 🟡 → ℹ️).

### Step 6: Summarize

```markdown
| Severity | Count | Top Issue |
|----------|-------|-----------|
| 🔴 Critical | N | e.g., Reflection.Emit usage |
| 🟡 Warning | N | e.g., Unannotated Activator.CreateInstance |
| ℹ️ Info | N | e.g., Expression.Compile uses interpreter |
```

## Validation Checklist

- [ ] All 🔴 Critical patterns from `critical-patterns.md` checked
- [ ] Topic references loaded only when matching signals detected
- [ ] Each finding includes concrete code fix
- [ ] Findings grouped by file, ordered by severity
- [ ] .NET version requirements noted where applicable
- [ ] No recommendations outside the .NET ecosystem
- [ ] Summary table included

## Common Pitfalls

| Pitfall | Correct Approach |
|---------|-----------------|
| Suppressing all AOT warnings blindly | Only suppress with `[UnconditionalSuppressMessage]` after manual verification |
| Using `#pragma warning disable` for trim warnings | `#pragma` is not preserved in IL — use `[UnconditionalSuppressMessage]` |
| Annotating with `DynamicallyAccessedMemberTypes.All` | Use the narrowest member type (e.g., `PublicParameterlessConstructor`) |
| Assuming `[RequiresUnreferencedCode]` fixes the problem | It only moves the warning to callers — redesign the API when possible |
| Testing only with `dotnet build`, not `dotnet publish` | Roslyn analyzers miss some warnings — always validate with `dotnet publish -r <RID>` |
| Marking a library `IsAotCompatible` without testing | Set up an AOT test app that publishes and exercises all APIs |
| Expecting EF Core to fully work with AOT | EF Core AOT support is experimental — use compiled models + precompiled queries and test thoroughly |

## Further Reading

- [Native AOT Deployment](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [Fixing AOT Warnings](https://learn.microsoft.com/dotnet/core/deploying/native-aot/fixing-warnings)
- [Fixing Trim Warnings](https://learn.microsoft.com/dotnet/core/deploying/trimming/fixing-warnings)
- [Making Libraries AOT-Compatible](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/)
- [Trimming Incompatibilities](https://learn.microsoft.com/dotnet/core/deploying/trimming/incompatibilities)
