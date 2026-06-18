---
name: find-untested-sources
description: >
  Parse-only C# analysis that pairs source files with referencing tests and
  emits JSON: `source_to_tests`, `untested` ordered by declaration count, and
  `suggested_test_path` from `ProjectReference` edges.
  USE FOR: where to write tests next, find untested files, list sources
  without tests, build a test-pairing map.
  DO NOT USE FOR: coverage (use `coverage-analysis`), CRAP risk ranking,
  assertion gaps.
license: MIT
---

# Find Untested Sources

## Purpose

Coverage tools answer "which lines were executed?" — they require a green build
and a passing test run, which is minutes-to-tens-of-minutes on a real repo.
The question this skill answers is different and much cheaper:

> _Which C# source files have no test file referencing any of their declared types?_

That's the question an agent asks **before** writing a new test — and it can be
answered statically in a few seconds by parsing every `.cs` file with the
Roslyn syntax API, with **no `Compilation`, no `MetadataReference`, and no
binding**. The output is a deterministic test-pairing map that lets the agent
pick the next file to test without reading the entire codebase first.

## When to Use

- User asks "where should I add tests?", "which files have no tests?", "find
  untested code", "give me a test gap list", "what's the next file to test".
- Before invoking a test-generation agent, to produce a prioritized worklist.
- After generating tests, to verify each new test file pairs to a source file.
- To enumerate "weakly paired" source files (only one referring test file) for
  follow-up depth checks.

## When Not to Use

- **Line/branch coverage** — use `coverage-analysis`.
- **CRAP-score / risk hotspots** — use `coverage-analysis`.
- **Are existing tests strong?** — use `test-gap-analysis` (mutation reasoning)
  or `assertion-quality`.
- **Tests for non-C# code** — this prototype is C#-only.

## Inputs

| Input | Required | Default | Description |
|-------|----------|---------|-------------|
| Repo root | Yes | — | Directory to scan recursively for `.cs` files. |
| `--top N` | No | all | Truncate the `untested` list to the top N entries by declaration count. |

### Prerequisites

- .NET SDK that supports file-based apps (`dotnet run script.cs`). Pinned in
  the repo's `global.json` (SDK 11 preview or later).
- No internet access required beyond the initial NuGet restore of
  `Microsoft.CodeAnalysis.CSharp` on first run.

## Usage

```powershell
# From the skill folder
dotnet run scripts/Find-UntestedSources.cs -- <repo-root> [--top N]

# Save the report
dotnet run scripts/Find-UntestedSources.cs -- <repo-root> > pairing.json

# Iterate the untested list, highest-API-surface first
$report = Get-Content pairing.json | ConvertFrom-Json
$report.untested | Select-Object -First 10 source, decl_count, suggested_test_path
```

Diagnostics go to stderr; JSON goes to stdout.

## Output Schema

```jsonc
{
  "repo": "<absolute path>",
  "elapsed_ms": 8883,
  "counts": {
    "source_files": 3036,
    "test_files": 867,
    "untested_files": 1852,
    "paired_files": 1184
  },
  "untested": [
    {
      "source": "src/Foo/Bar.cs",
      "decl_count": 8,            // # of type declarations in the file
      "suggested_test_path":      // mirror of source under a discovered test project
        "tests/Foo.Tests/Bar/BarTests.cs"
    }
  ],
  "source_to_tests": {
    "src/Foo/Baz.cs": [
      "tests/Foo.Tests/BazTests.cs",
      "tests/Foo.IntegrationTests/Scenarios/BazScenarios.cs"
    ]
  }
}
```

## How It Works

1. **File discovery** — recursive directory walk pruning `bin/`, `obj/`,
   `node_modules/`, `.git/`, `.vs/`, `packages/`, and any dotted subdir.
   Skips generated files (`.g.cs`, `.Designer.cs`, `.AssemblyInfo.cs`).

2. **Test vs source classification** — walks up to the nearest `.csproj` and
   marks it as a test project if (a) the project name ends in `.Tests`,
   `.Test`, `.UnitTests`, `.IntegrationTests`, `.E2E`, `.EndToEnd`, `.Spec`,
   `.Specs`, or (b) the file content references `Microsoft.NET.Test.Sdk`,
   `MSTest.Sdk`, `Microsoft.Testing.Platform`, `xunit`, `NUnit`, `TUnit`, or
   `<IsTestProject>true</IsTestProject>`.

3. **Source index (parallel)** — for each source file, parse with
   `CSharpSyntaxTree.ParseText` (syntax only, no compilation). Walk every
   `BaseTypeDeclarationSyntax` and `DelegateDeclarationSyntax` and record
   `(ShortName, EnclosingNamespace, FilePath)`.

4. **Test scan (parallel)** — for each test file, parse, collect `using`
   directives + enclosing namespace, walk every `IdentifierToken`, look it up
   in the short-name index, and **disambiguate strictly**: an identifier is
   attributed to a declaration only if the declaration's namespace matches one
   of the test file's `using` directives, the enclosing namespace, or a
   prefix of them. Identifiers that don't resolve under that constraint are
   dropped (avoids the noise where common names like `Settings` or `Context`
   would otherwise match every project that happens to declare them).

5. **Pairing & suggestion** — invert into `source → [tests]`. Build a
   production-to-test project map from `<ProjectReference>` entries in test
   `.csproj` files; for each untested source, mirror its in-project relative
   path under the referencing test project and append `Tests.cs` to suggest a
   path.

6. **JSON emit** — ordered by declaration count desc, then alphabetical.

## Limitations (be honest with the agent)

This is a static, parse-only heuristic. It deliberately trades a small amount
of accuracy for orders-of-magnitude lower cost than coverage. Known gaps:

- **Reflection-driven tests** that exercise a type only via
  `Type.GetType(...)` / `Activator.CreateInstance` won't be detected — the
  type's short name never appears in the test source.
- **DI-resolved types** referenced only by `IServiceProvider.GetRequiredService<T>()`
  where `T` is an interface and the implementation isn't named in the test.
- **Extension methods** invoked as instance methods. The extension class is
  not named, only the method, so the source file declaring the static class
  is not credited.
- **`var`, target-typed `new()`, and pattern matching** lose the type token;
  the file-level union usually still catches it through other references.
- **Cross-language**: any source file driven by JSON/YAML test fixtures, code
  generators, or compiled-only references is not detected.

For these cases, run actual coverage (`coverage-analysis`) on the unpaired
candidates the agent has already triaged.

## Outputs the agent should consume

- `untested[*].source` — pick the next source file to test (highest
  `decl_count` first).
- `untested[*].suggested_test_path` — drop-in target for the new test file;
  honors the test project that already `<ProjectReference>`s the source's
  project, so `dotnet sln add` is not needed.
- `source_to_tests` — verify a newly written test file lands in the list for
  the intended source.
