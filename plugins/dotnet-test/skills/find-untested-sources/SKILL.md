---
name: find-untested-sources
description: >
  MANDATORY for static source-to-test pairing: find or list source files/modules
  without corresponding tests, or suggest test locations from repository
  structure. Invoke even for a tiny package; do not substitute manual globbing.
  Uses Roslyn for C#/.NET and tree-sitter for Python, TS/JS, Go, Java, Rust,
  Ruby, Kotlin, Swift, PowerShell, and C++. DO NOT USE FOR: real
  line/branch/Cobertura data, coverage-backed test priorities, CRAP risk, or
  grading existing tests.
license: MIT
---

# Find Untested Sources

## Purpose

Coverage tools answer "which lines were executed?" using instrumented execution.
This skill answers a different, static question:

> _Which source files have no detected test reference to any of their declared
> types/symbols?_

That's the question an agent asks **before** writing a new test — and it can be
answered by parsing source files, with **no target-project build,
no dependency resolution, and no compilation**. The output is a deterministic
heuristic reference map, not proof that behavior is tested. Analyzer runtime
dependencies remain prerequisites; launching the analyzer does not compile the
analyzed project.

## Two engines — pick one

This skill ships two analyzers with **different legacy schemas**. Pick the
engine first and use its matching keys. Both also support the explicitly
selected `--output-format json-v1` shared envelope; defaults remain legacy.

| Engine | Script | Use when |
|--------|--------|----------|
| **Roslyn (C#)** | `scripts/Find-UntestedSources.cs` | The repo is **.NET-only**. Parses every `.cs` file with the Roslyn syntax API and does strict **namespace disambiguation**, so it is materially more accurate on duplicated short names like `Settings` or `Context`. |
| **tree-sitter (polyglot)** | `scripts/find_untested_sources.py` | The repo is **not exclusively C#**, or you want one tool across C#, Python, TypeScript/JavaScript, Go, Java, Rust, Ruby, Kotlin, Swift, PowerShell, and C++. |

For a .NET-only repository, **prefer the Roslyn engine** — its namespace-aware
pairing beats the polyglot engine's identifier overlap.

## Required workflow

1. Use the narrowest repository or package root named by the caller. Do not scan
   a parent workspace when the request identifies a subdirectory.
2. Execute the appropriate analyzer once. Do not replace analyzer execution with
   manual globbing, filename matching, or visual inspection.
   For polyglot analysis, pass `--include-tested` when the answer must distinguish
   paired sources from unpaired sources.
   "Static pairing only" prohibits compiling the target repository and running
   its tests; it does not prohibit launching this skill's parse-only analyzer.
   State that distinction briefly when the caller also says "do not build."
   Treat analyzer dependencies as environment prerequisites: do not install
   packages, try the wrong engine, build the repository, or fall back to a manual
   scan when an analyzer invocation fails. Report the prerequisite failure instead.
3. Check exit status, the selected engine's JSON shape and stderr diagnostics
   before using results. The legacy payload does not certify completeness.
   Preserve supported classifications and suggested paths; do not infer
   unpaired sources from failed or omitted analysis.
4. When the caller named a subdirectory, prefix analyzer-relative paths with
   that subdirectory so reported paths are workspace-relative.
5. Report the requested result plus the static-pairing coverage caveat. Do not
   append build, package-install, test-run, or coverage commands. When paired
   sources exist, name their referring test files so the classification is
   auditable; a reference does not prove an assertion reaches the behavior.

## Output compatibility and completeness

Default output stays legacy: Roslyn uses `repo`, `counts`, `untested` and
`source_to_tests`; Python uses `repo_root`, `summary`, `untested_sources` and
optional `tested_sources`. Do not feed one engine's legacy JSON to the other's
consumer or silently change its output mode.

For JSON v1, pass `--output-format json-v1` to the analyzer (after `--` when
launching the C# file-based app). The envelope contains `schema_version: "1"`,
`tool`, `tool_version`, `status`, `scope`, `inputs`, `diagnostics` and `data`.
Both engines expose `data.repo_root`, `summary`, `untested_sources`,
`orphan_tests`, `unassessed_files` and optional `tested_sources` with
`--include-tested`. Roslyn legacy output always includes `source_to_tests`;
Python legacy includes `tested_sources` only when requested. Declaration
semantics remain engine-specific; matching keys do not make counts equivalent.

Exit 0 means complete, 3 partial, and 2 an analyzer error in v1. Legacy mode
fails without a success-shaped payload when incomplete results cannot be
represented. Surface diagnostics and unassessed files, including ambiguous or
unreadable project classification, rather than calling them paired or unpaired.
Host/SDK launch failures may have no envelope; empty/non-JSON output is not a
successful empty analysis. Do not automatically install dependencies after failure.

Input hashes bind bytes read, not a commit; `scope.source_revision` is null.
Respect the root, language filters and explicit truncation metadata in `scope`;
a limited list is not the whole inventory. Rust inline `test_regions` can share
a physical file with source declarations, so source/test counts overlap.

## When to Use

- User asks "where should I add tests based on source pairing?", "which files
  have no tests?", "find unpaired source files", or "give me a static test gap
  list".
- Before invoking a test-generation agent, to produce a source-pairing worklist.
- After generating tests, to verify each new test file pairs to a source file.
- To enumerate "weakly paired" source files (only one referring test) for
  follow-up depth checks.

## When Not to Use

- **Line/branch coverage** — use `coverage-analysis`.
- **Priorities derived from real coverage data** — use `coverage-analysis`.
- **CRAP-score / risk hotspots** — use `coverage-analysis`.
- **Are existing tests strong?** — use `test-gap-analysis` (mutation reasoning)
  or `assertion-quality`.

## Roslyn engine (C#)

### Prerequisites

- .NET SDK that supports file-based apps (`dotnet run script.cs`). Pinned in the
  repo's `global.json` (SDK 11 preview or later).
- No internet access required beyond the initial NuGet restore of
  `Microsoft.CodeAnalysis.CSharp` on first run.

### Usage

```powershell
# From the skill folder
dotnet run scripts/Find-UntestedSources.cs -- <repo-root> [--top N]

# Save the report after separately building the file-based analyzer.
# Resolve its DLL from the successful build output, not a guessed framework path.
dotnet <built-analyzer.dll> <repo-root> > pairing.json

# Iterate the untested list, highest-API-surface first
$report = Get-Content pairing.json | ConvertFrom-Json
$report.untested | Select-Object -First 10 source, decl_count, suggested_test_path
```

Diagnostics go to stderr; JSON goes to stdout.
This separation applies to the analyzer process, not to the `dotnet run`
build/restore launcher. Capture the build separately, then invoke the built
analyzer for machine-readable output. Check its exit code before parsing the
report; do not strip arbitrary build-log lines and call the remainder valid JSON.

### Output schema

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

### How it works

1. **File discovery** — recursive walk pruning `bin/`, `obj/`, `node_modules/`,
   `.git/`, `.vs/`, `packages/`, and any dotted subdir. Skips generated files
   (`.g.cs`, `.Designer.cs`, `.AssemblyInfo.cs`).
2. **Test vs source classification** — walks up to the nearest `.csproj` and
   marks it a test project if the project name ends in `.Tests`, `.Test`,
   `.UnitTests`, `.IntegrationTests`, `.E2E`, `.EndToEnd`, `.Spec`, `.Specs`, or
   the content references `Microsoft.NET.Test.Sdk`, `MSTest.Sdk`,
   `Microsoft.Testing.Platform`, `xunit`, `NUnit`, `TUnit`, or
   `<IsTestProject>true</IsTestProject>`.
3. **Source index (parallel)** — parse each source file with
   `CSharpSyntaxTree.ParseText` (syntax only, no compilation); record every
   `BaseTypeDeclarationSyntax` / `DelegateDeclarationSyntax` as
   `(ShortName, EnclosingNamespace, FilePath)`.
4. **Test scan (parallel)** — parse each test file, collect `using` directives +
   enclosing namespace, walk every `IdentifierToken`, look it up in the
   short-name index, and **disambiguate strictly**: an identifier is attributed
   only if the declaration's namespace matches one of the test file's `using`
   directives, the enclosing namespace, or a prefix of them. This avoids noise
   where common names like `Settings` or `Context` match every project.
5. **Pairing & suggestion** — invert into `source → [tests]`. Build a
   production-to-test project map from `<ProjectReference>` entries; for each
   untested source, mirror its in-project relative path under the referencing
   test project to suggest a path.
6. **JSON emit** — ordered by declaration count desc, then alphabetical.

## Polyglot engine (tree-sitter)

### Prerequisites

- Python 3.10+.
- `pip install tree-sitter-language-pack` (single self-contained wheel that
  bundles parsers for 300+ languages and the high-level `process()` API). No
  native build, no per-language grammar install.

### Usage

```powershell
# From the skill folder
python scripts/find_untested_sources.py <repo-root>

# Restrict to a language (repeatable)
python scripts/find_untested_sources.py <repo-root> --lang python --lang typescript

# Truncate the report (top 20 by declared API surface)
python scripts/find_untested_sources.py <repo-root> --limit-untested 20 > pairing.json

# Iterate, highest-API-surface first
$report = Get-Content pairing.json | ConvertFrom-Json
$report.untested_sources | Select-Object -First 10 path, declaration_count, suggested_test_path
```

Pass `--include-tested` to additionally emit `tested_sources` (omitted by
default to keep the payload small for LLM consumption). Diagnostics go to
stderr; JSON goes to stdout.

### Output schema

```jsonc
{
  "repo_root": "<absolute path>",
  "summary": {
    "source_files": 3138,
    "test_files": 761,
    "tested_source_files": 1419,
    "untested_source_files": 1719,
    "orphan_test_files": 15,
    "languages": ["csharp"]
  },
  "untested_sources": [
    {
      "path": "src/Foo/Bar.cs",
      "language": "csharp",
      "declaration_count": 8,
      "declarations": ["Bar", "BarOptions", "IBar", "..."],
      "suggested_test_path": "src/Foo/BarTests.cs"
    }
  ],
  "orphan_tests": [
    { "path": "tests/SomeIntegrationTest.cs", "language": "csharp" }
  ]
}
```

### How it works

1. **File discovery** — recursive walk pruning common build/vendor dirs (`bin`,
   `obj`, `node_modules`, `target`, `dist`, `build`, `vendor`, `__pycache__`,
   `.venv`, `.git`, …) and generated files (`.d.ts`, `.g.cs`, `.Designer.cs`,
   `_pb2.py`, `*.min.js`, `AssemblyInfo.cs`, …).
2. **Language detection** — `detect_language_from_path` maps the extension to a
   supported language; unknown extensions are skipped.
3. **Test-vs-source classification** — per-language path heuristics:

   | Language | Test rule |
   |---|---|
   | Python | path contains `tests/`/`test/`; or filename starts with `test_` or ends `_test.py`; or `conftest.py`. |
   | JS/TS/TSX | path contains `__tests__`, `tests`, `test`, `spec`, `e2e`; or filename contains `.test.`/`.spec.`. |
   | Go | filename ends `_test.go`. |
   | Java | path contains `test`/`tests`; or filename ends `Test.java`/`Tests.java`. |
   | Rust | path contains `tests/`/`benches/`, or inline `#[cfg(test)]` regions in source files; mixed files retain their production declarations. |
   | C# | path contains `tests/`; or project segment ends `.Tests`/`.Test`/`.UnitTests`/`.IntegrationTests`; or filename ends `Tests`/`Test`. |
   | Ruby | path contains `spec/`/`test/`; or filename ends `_spec.rb`/`_test.rb`. |
   | Kotlin | path contains `test/`/`tests/`/`spec/`; or filename ends `Test.kt`/`Tests.kt`/`Spec.kt`. |
   | Swift | path contains `test/`/`tests/`/`uitests/`/`integrationtests/` (case-insensitive); or filename ends `Test.swift`/`Tests.swift`. |
   | PowerShell | path contains `test/`/`tests/`/`pester/`; or filename ends `.Tests.ps1`/`.Test.ps1`. |
   | C++ | path contains `test/`/`tests/`/`testing/`; or filename starts `test_` or ends `_test.cpp`/`_tests.cpp`. |

4. **Per-file extraction** — `process(text, ProcessConfig(structure, imports,
   symbols))` returns declared items, raw import statements, and a flat declared
   -name list.
5. **Pairing** — for each test file, union **import resolution** (per language,
   e.g. Python `from pkg.mod import x` → `pkg/mod.py`; Java `import a.b.C;` →
   `a/b/C.java`; C# `using` is namespace-not-file, so a no-op) with **identifier
   overlap** from parsed code tokens, excluding comments and string contents.
   TS/TSX imports and Rust inline test regions contribute reference evidence,
   not proof that a test reaches or asserts a particular behavior.
6. **JSON emit** — `untested_sources` ordered by declaration count descending.

## Limitations (be honest with the agent)

Both engines are static, parse-only heuristics, not semantic compilation or
behavioral coverage. Known gaps:

- **Reflection / DI-resolved types** referenced only via a string name or
  container resolution won't be detected — the type's short name never appears
  in the test source.
- **Extension methods** invoked as instance methods (C#): the declaring static
  class is not named, so its file is not credited.
- **`var`, target-typed `new()`, pattern matching** lose the type token; the
  file-level union usually still catches it through other references.
- **Short identifier names** (polyglot, < 4 chars) are dropped to avoid noisy
  pairings on names like `id`, `db`, `Tag`.
- **Monorepo path aliases** (TS path mapping, Java module-info) are not
  resolved; a suffix-match fallback may pick the wrong source if two files share
  a trailing path segment.
- **Import/token approximations** are not semantic symbol resolution. Parser
  diagnostics and unsupported constructs reduce completeness; do not claim
  resolved symbol identity or behavioral coverage from token overlap.

Report these limitations in a static-only request. Actual coverage requires a
separately authorized execution workflow (`coverage-analysis` for .NET, native
tooling elsewhere); do not start it merely to repair a pairing limitation.

Always label the final result as a static pairing heuristic, not evidence of
line or branch coverage. Include that caveat even when every requested source
file has an obvious matching or missing test.

## Outputs the agent should consume

- `untested[*].source` / `untested_sources[*].path` — pick the next source file
  to test (highest declaration count first).
- `*.suggested_test_path` — drop-in target for the new test file; the Roslyn
  engine honors the test project that already `<ProjectReference>`s the source's
  project, so `dotnet sln add` is not needed. The polyglot engine may suggest a
  co-located test when no test root is discoverable. When a source sibling is
  already paired, its test directory is the established convention and must be
  reused for the missing sibling rather than falling back to source co-location.
- `source_to_tests` (Roslyn) / `--include-tested` `tested_sources` (polyglot) —
  verify a newly written test file lands in the list for the intended source.
- `orphan_tests` (polyglot) — tests that don't reference any same-language
  source file; useful for triaging stale or integration-only tests.
