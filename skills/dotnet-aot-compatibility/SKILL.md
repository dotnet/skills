---
name: dotnet-aot-compatibility
description: >-
  Scans .NET code for Native AOT and trimming compatibility issues and provides
  concrete fixes. Use when preparing an application or library for PublishAot,
  diagnosing IL trimming/AOT warnings (IL2026, IL3050, IL2070, etc.), migrating
  from reflection-heavy patterns to source generators, or evaluating library
  compatibility with Native AOT on .NET 8–10.
---

# .NET AOT Compatibility

Detect and fix .NET Native AOT and trimming compatibility issues in application and library code. Patterns are sourced from official Microsoft documentation, the .NET Blog, and real-world library migration case studies (OpenTelemetry, StackExchange.Redis, Microsoft.IdentityModel, Microsoft.Extensions).

## Purpose

Scan C# / .NET code and produce a prioritized list of AOT compatibility findings with concrete fix suggestions. The goal is a **zero-warning AOT publish** — if an application produces no AOT warnings at publish time, it will behave the same after AOT as it does without AOT.

## When to Use

- Preparing an application for `<PublishAot>true</PublishAot>`
- Diagnosing IL trimming or AOT warnings (IL2026, IL2070, IL3050, IL3058, IL2104, etc.)
- Migrating reflection-heavy code to source-generated alternatives
- Evaluating whether a project's dependencies are AOT-compatible
- Making a library AOT-compatible with `<IsAotCompatible>true</IsAotCompatible>`
- Reviewing a PR that touches reflection, serialization, DI, or generic type construction

## When Not to Use

- **Runtime performance tuning** — use `dotnet-performance-patterns` for hot-path optimization
- **Algorithm design** — this skill targets API usage patterns, not algorithmic complexity
- **Projects that will never use AOT** — don't force AOT constraints on code that will always run with JIT
- **WPF or Windows Forms applications** — these frameworks are not AOT-compatible in .NET 10

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Source code | Yes | C# files, project files, or repository paths to scan |
| Target framework | Recommended | .NET version (affects which patterns and source generators are available) |
| Project type | Recommended | App vs library — affects annotation strategy |
| Scan depth | Optional | `critical-only` (default), `standard`, or `comprehensive` |

## Workflow

### Step 1: Load Critical Patterns

Always load `references/critical-patterns.md`. These patterns represent hard failures — code that **will** crash, throw, or silently produce wrong results in a Native AOT application. Flag every match unconditionally.

### Step 2: Detect Code Signals and Load Topic References

Scan the code for signals that indicate which topic-specific reference files to load. Only load files relevant to the code under review.

| Signal in Code | Load Reference | Examples |
|----------------|----------------|----------|
| `JsonSerializer`, `Newtonsoft`, `JsonConvert`, `IConfiguration`, `IOptions<`, `Bind(`, `[JsonSerializable` | [serialization-and-config.md](references/serialization-and-config.md) | STJ source gen, config binding generator, options validation |
| `Type.GetType`, `GetMethod(`, `GetProperties(`, `Activator.CreateInstance`, `Assembly.Load`, `[DynamicallyAccessedMembers`, `IServiceCollection`, DI registration | [reflection-and-di.md](references/reflection-and-di.md) | Annotation workflow, DI patterns, factory methods |
| `MakeGenericType`, `MakeGenericMethod`, `typeof(GenericType<>)`, `Expression.`, `Linq.Expressions`, struct generics | [generics-and-types.md](references/generics-and-types.md) | Value type instantiation, expression compilation, generic constraints |
| `<PackageReference`, `using` statements for known libraries (EF Core, gRPC, SignalR, etc.) | [library-compatibility.md](references/library-compatibility.md) | Library AOT status, required configuration, alternatives |
| `.csproj`, `Directory.Build.props`, `PublishAot`, `IsAotCompatible`, `IsTrimmable`, CI/CD files | [project-setup.md](references/project-setup.md) | MSBuild properties, analyzer setup, CI validation |

**Scan depth controls loading:**
- `critical-only`: Only Step 1 (critical patterns)
- `standard` (default): Steps 1 + 2 (critical + detected topics)
- `comprehensive`: Load all reference files

### Step 3: Scan Code Against Loaded Patterns

For each loaded pattern, check whether the code under review exhibits the anti-pattern. Match on:
- Specific API calls (e.g., `Assembly.LoadFrom`, `Type.MakeGenericType`, `new Regex(`)
- Structural patterns (e.g., reflection inside static constructors, `dynamic` keyword usage)
- Missing source generation (e.g., `JsonSerializer.Serialize` without `JsonTypeInfo`)
- Missing annotations (e.g., `Activator.CreateInstance(type)` without `[DynamicallyAccessedMembers]`)
- Project configuration gaps (e.g., `PublishAot` without `TrimmerSingleWarn` set to false)

### Step 4: Classify and Prioritize Findings

| Severity | Criteria | Action |
|----------|----------|--------|
| 🔴 **Critical** | Will crash, throw, or produce corrupt data in AOT. No workaround except code change. | Must fix |
| 🟡 **Warning** | Produces IL warnings at publish. May work but is not guaranteed. | Should fix for zero-warning publish |
| ℹ️ **Info** | Sub-optimal for AOT (larger binary, slower interpreted path) but functional. | Consider fixing |

### Step 5: Generate Fix Suggestions

For each finding, provide:
1. **What**: The incompatible pattern detected (one sentence)
2. **Why**: What goes wrong in AOT (crash, silent failure, warning code)
3. **Fix**: Concrete code change — show ❌ current code and ✅ AOT-compatible replacement
4. **Version**: Minimum .NET version required for the fix

Group findings by file, then by severity (🔴 first, then 🟡, then ℹ️).

### Step 6: Summarize

End with a summary table:

```markdown
| Severity | Count | Top Issue |
|----------|-------|-----------|
| 🔴 Critical | N | e.g., Reflection.Emit usage |
| 🟡 Warning | N | e.g., Unannotated Activator.CreateInstance |
| ℹ️ Info | N | e.g., Expression.Compile uses interpreter |
```

## Validation

- [ ] All 🔴 Critical patterns from `critical-patterns.md` were checked
- [ ] Topic reference files were loaded only when matching signals were detected
- [ ] Each finding includes a concrete code fix, not just a warning
- [ ] Findings are grouped by file, ordered by severity
- [ ] .NET version requirements are noted when a fix requires a specific version
- [ ] No findings suggest rewriting code outside the .NET ecosystem
- [ ] Project setup recommendations include MSBuild properties and analyzer configuration
- [ ] Summary table is included at the end

## Common Pitfalls

| Pitfall | Why It's Wrong | Correct Approach |
|---------|---------------|-----------------|
| Suppressing all AOT warnings blindly | Hides real issues that will crash in production | Only suppress with `[UnconditionalSuppressMessage]` after manual verification |
| Using `#pragma warning disable` for trim warnings | Not preserved in compiled assembly — trimmer ignores it | Use `[UnconditionalSuppressMessage]` which persists in IL |
| Annotating with `DynamicallyAccessedMemberTypes.All` | Preserves everything, defeats trimming, may cause cascading issues | Use the narrowest member type needed (e.g., `PublicParameterlessConstructor`) |
| Assuming `[RequiresUnreferencedCode]` fixes the problem | It only moves the warning to callers — the code is still incompatible | Redesign the API to be trim-compatible when possible |
| Testing only with `dotnet build`, not `dotnet publish` | Build-time Roslyn analyzers catch most but not all warnings | Always validate with `dotnet publish -r <RID>` for full ILC analysis |
| Marking a library `IsAotCompatible` without testing | The flag is a promise — breaking it breaks downstream consumers | Set up an AOT test app that publishes and exercises your library's APIs |
| Expecting EF Core to fully work with AOT | EF Core AOT support is experimental (compiled models + precompiled queries required) | Use `dotnet ef dbcontext optimize --precompile-queries --nativeaot` and test thoroughly |

## Further Reading

- [Native AOT Deployment](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) — official docs
- [How to Make Libraries Compatible with Native AOT](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/) — .NET Blog case studies
- [Introduction to AOT Warnings](https://learn.microsoft.com/dotnet/core/deploying/native-aot/fixing-warnings) — warning code reference
- [Fixing Trim Warnings](https://learn.microsoft.com/dotnet/core/deploying/trimming/fixing-warnings) — annotation workflow
- [Known Trimming Incompatibilities](https://learn.microsoft.com/dotnet/core/deploying/trimming/incompatibilities) — hard limits
