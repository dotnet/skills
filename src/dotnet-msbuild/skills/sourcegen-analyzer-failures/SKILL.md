---
name: sourcegen-analyzer-failures
description: "Diagnose and fix source generator and Roslyn analyzer failures in .NET builds. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds fail with CS8785 (source generator errors), AD0001 (analyzer exceptions), RS-prefixed errors, or when source generators produce no output. Covers generator crashes, analyzer exceptions, debugging with /p:ReportAnalyzer=true and binlog analysis, version mismatches, and TFM incompatibilities. DO NOT use for build errors unrelated to analyzers/generators (use common-build-errors instead)."
---

# Source Generator & Analyzer Failure Diagnosis

## Overview

Source generators and Roslyn analyzers run during compilation inside the Csc task. When they fail, the errors can be cryptic. This skill covers diagnosing and fixing these failures.

**Related skills:**
- `common-build-errors` — for general CS/MSB errors not related to generators/analyzers
- `analyzer-performance` — for slow (but working) analyzers, not crashes
- `binlog-failure-analysis` — for deep binlog analysis techniques

---

## Source Generator Failures

### CS8785: Generator 'X' failed to generate source

**What it means**: A source generator threw an unhandled exception during execution.

**Diagnosis steps:**

1. **Check the build output** for the full exception stack trace. MSBuild typically prints the inner exception after the CS8785 message.

2. **Generate a binlog** for deeper analysis:
   ```
   dotnet build /bl:generator-debug.binlog
   ```
   In the binlog, search for CS8785 or the generator name. The Csc task messages will contain the full exception details.

3. **Enable detailed analyzer output:**
   ```
   dotnet build /p:ReportAnalyzer=true
   ```
   This adds timing and error information for each analyzer and generator to the build output.

**Common root causes:**

#### Missing or wrong input files
The generator expects specific files (e.g., `.json`, `.xml`, `AdditionalFiles`) that are missing or have wrong content.

```xml
<!-- Fix: Ensure AdditionalFiles are included -->
<ItemGroup>
  <AdditionalFiles Include="appsettings.json" />
</ItemGroup>
```

#### Target framework incompatibility
The generator package targets `netstandard2.0` but has dependencies that aren't available, or the generator's code uses APIs not in `netstandard2.0`.

```xml
<!-- Check the generator package's target framework -->
<!-- If the generator requires a newer runtime, ensure your build environment has it -->
```

#### Generator package version mismatch with compiler
Source generators depend on specific versions of `Microsoft.CodeAnalysis`. If the compiler version (determined by the SDK) is older than what the generator expects, it crashes.

```xml
<!-- Fix: Update the SDK or pin a compatible generator version -->
<!-- global.json -->
{
  "sdk": {
    "version": "8.0.400",
    "rollForward": "latestFeature"
  }
}

<!-- Or pin an older generator version compatible with your SDK -->
<PackageReference Include="SomeGenerator" Version="1.2.0" />
```

#### Concurrent build access
In parallel builds, generators may fail if they write to shared state or temporary files without locking.

```
<!-- Fix: Ensure UseArtifactsOutput or separate IntermediateOutputPath per TFM -->
<PropertyGroup>
  <UseArtifactsOutput>true</UseArtifactsOutput>
</PropertyGroup>
```

### No Output from Source Generator (Silent Failure)

**Symptom**: The generator runs without errors but produces no files. Types that should be generated are missing, causing `CS0246` or `CS0103` errors.

**Diagnosis:**

1. **Verify the generator is loaded:**
   ```
   dotnet build /p:ReportAnalyzer=true
   ```
   Look for the generator name in the output. If it's not listed, the package isn't being loaded.

2. **Check the package reference:**
   ```xml
   <!-- Generator packages need specific asset configuration -->
   <PackageReference Include="MyGenerator" Version="1.0.0"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```

3. **Check binlog for generator execution:**
   Load the binlog → search for the Csc task → check `get_task_analyzers` for generator execution details.

4. **Verify the generator's trigger:**
   Most generators use `[Generator]` attribute and `IIncrementalGenerator`. They need specific syntax triggers (attributes, partial classes) in your code. Check the generator's documentation for required trigger patterns.

**Common fixes:**

```xml
<!-- Ensure the generator package is correctly referenced -->
<PackageReference Include="MyGenerator" Version="1.0.0" PrivateAssets="all" />

<!-- If it's a project reference to a generator project -->
<ProjectReference Include="..\MyGenerator\MyGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### Generator Works Locally but Fails in CI

**Common causes:**

1. **SDK version difference** — CI has a different .NET SDK version. Pin via `global.json`:
   ```json
   {
     "sdk": {
       "version": "8.0.400",
       "rollForward": "latestPatch"
     }
   }
   ```

2. **Missing AdditionalFiles** — Files not tracked in source control. Check `.gitignore`.

3. **Path length issues** — Windows CI agents may have long base paths. Use `UseArtifactsOutput` or short output paths.

---

## Analyzer Failures

### AD0001: Analyzer 'X' threw an exception

**What it means**: An analyzer's `Initialize`, `RegisterXxxAction`, or analysis callback threw an unhandled exception.

**Diagnosis:**

1. **Find the full exception** in build output or binlog. The AD0001 message includes the exception type and stack trace.

2. **Identify the analyzer package:**
   ```
   dotnet build /p:ReportAnalyzer=true
   ```

3. **Common causes:**
   - **Version incompatibility**: Analyzer compiled against a newer `Microsoft.CodeAnalysis` than the current compiler provides.
   - **Missing dependencies**: Analyzer has runtime dependencies not present in the build.
   - **Null reference in analyzer code**: The analyzer doesn't handle edge cases in your code (empty files, partial classes, unusual syntax).

**Fixes:**

```xml
<!-- Fix 1: Update the analyzer package -->
<PackageReference Include="SomeAnalyzer" Version="latest-stable" PrivateAssets="all" />

<!-- Fix 2: Suppress the specific analyzer temporarily -->
<PropertyGroup>
  <NoWarn>$(NoWarn);AD0001</NoWarn>
</PropertyGroup>

<!-- Fix 3: Disable a specific analyzer entirely -->
<ItemGroup>
  <Analyzer Remove="@(Analyzer)" Condition="'%(Filename)' == 'ProblematicAnalyzer'" />
</ItemGroup>

<!-- Fix 4: Disable all analyzers during build (for unblocking) -->
<PropertyGroup>
  <RunAnalyzers>false</RunAnalyzers>
</PropertyGroup>
```

### RS Errors (Roslyn SDK Diagnostic Rules)

Common RS errors in analyzer/generator development:

| Error | Meaning | Fix |
|-------|---------|-----|
| `RS1001` | Missing `DiagnosticAnalyzer` export | Add `[DiagnosticAnalyzer(LanguageNames.CSharp)]` |
| `RS1004` | Recommend adding a suppressor | Implement `DiagnosticSuppressor` if applicable |
| `RS1025` | Configure generated code analysis | Set `generated_code = true` in `.editorconfig` |
| `RS1026` | Enable concurrent execution | Call `context.EnableConcurrentExecution()` in `Initialize` |
| `RS2008` | Analyzer release tracking | Add analyzer release tracking files |

### Analyzer Version Compatibility Matrix

| .NET SDK | Roslyn Version | Max Supported Analyzer API |
|----------|---------------|---------------------------|
| 6.0.x | 4.0.x–4.4.x | `Microsoft.CodeAnalysis` 4.x |
| 7.0.x | 4.4.x–4.8.x | `Microsoft.CodeAnalysis` 4.x |
| 8.0.x | 4.8.x–4.12.x | `Microsoft.CodeAnalysis` 4.x |
| 9.0.x | 4.12.x+ | `Microsoft.CodeAnalysis` 4.x |

**Key rule**: Analyzers/generators must target `netstandard2.0` and depend on a `Microsoft.CodeAnalysis` version **less than or equal to** the version shipped with the SDK.

---

## Debugging Techniques

### 1. `/p:ReportAnalyzer=true`

Adds per-analyzer/generator timing and status to build output:

```
dotnet build /p:ReportAnalyzer=true
```

Output shows:
- Each analyzer/generator name
- Execution time
- Any errors or warnings produced

### 2. Binlog Analysis

```
dotnet build /bl:analyzer-debug.binlog
```

In the binlog:
- Find the `Csc` task for the failing project
- Use `get_task_analyzers` to extract analyzer/generator execution data
- Check for exception messages in the task's output messages

### 3. Isolating the Failing Analyzer

```xml
<!-- Temporarily disable all analyzers except the one you're debugging -->
<PropertyGroup>
  <RunAnalyzers>false</RunAnalyzers>
</PropertyGroup>
<ItemGroup>
  <!-- Re-enable only the one you're investigating -->
  <PackageReference Include="OnlyThisAnalyzer" Version="1.0.0" PrivateAssets="all" />
</ItemGroup>
```

### 4. Checking Analyzer Loading

If an analyzer isn't running at all, verify it's being loaded:

```xml
<!-- Check that the analyzer DLL is in the Analyzer item group -->
<!-- In binlog: search for the Analyzer item type in the evaluation -->
```

### 5. Verbose Compiler Logging

For deep Csc-level debugging:
```
dotnet build /p:CscVerbosity=detailed /bl:verbose.binlog
```

---

## Quick-Reference: Error → Fix

| Error | Likely Cause | Quick Fix |
|-------|-------------|-----------|
| `CS8785` | Generator exception | Update generator package; check AdditionalFiles; check SDK version |
| `AD0001` | Analyzer exception | Update analyzer package; suppress with NoWarn temporarily |
| `CS0246` after adding generator | Generator not loaded | Check `OutputItemType="Analyzer"` on PackageReference |
| `CS0103` for generated types | Generator produced no output | Verify trigger syntax (attributes, partial classes) |
| Generator works in VS but not CLI | Different SDK/compiler version | Pin SDK in `global.json` |
| Analyzers slow but not crashing | Not a failure — see `analyzer-performance` skill | Use `/p:ReportAnalyzer=true` for timing |
| `RS1026` | Missing concurrent execution | Add `context.EnableConcurrentExecution()` |
