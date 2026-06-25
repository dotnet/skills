---
name: find-untested-sources
description: >
  Parse-only static analysis that pairs source files with the tests referencing
  them and emits JSON listing untested files ordered by API surface, each with a
  suggested_test_path. Roslyn engine for C#/.NET (namespace-aware), tree-sitter
  engine for polyglot repos (Python, TS/JS, Go, Java, Rust, Ruby).
  USE FOR: where to write tests next, which files have no tests, find untested
  code, build a source-to-test pairing map, prioritized test-gap worklist.
  DO NOT USE FOR: line/branch coverage or CRAP risk (use coverage-analysis);
  whether existing tests are strong (use test-gap-analysis or assertion-quality).
license: MIT
---

# Find Untested Sources

## Purpose

Coverage tools answer "which lines were executed?" — they require a green build
and a passing test run, which is minutes-to-tens-of-minutes on a real repo. The
question this skill answers is different and much cheaper:

> _Which source files have no test file referencing any of their declared
> types/symbols?_

That's the question an agent asks **before** writing a new test — and it can be
answered statically in a few seconds by parsing source files, with **no build,
no dependency resolution, and no compilation**. The output is a deterministic
test-pairing map that lets the agent pick the next file to test without reading
the entire codebase first.

## Two engines — pick one

This skill ships two interchangeable analyzers with a compatible JSON contract:

| Engine | Script | Use when |
|--------|--------|----------|
| **Roslyn (C#)** | `scripts/Find-UntestedSources.cs` | The repo is **.NET-only**. Parses every `.cs` file with the Roslyn syntax API and does strict **namespace disambiguation**, so it is materially more accurate on duplicated short names like `Settings` or `Context`. |
| **tree-sitter (polyglot)** | `scripts/find_untested_sources.py` | The repo is **not exclusively C#**, or you want one tool across Python, TypeScript/JavaScript, Go, Java, Rust, Ruby, and C#. |

For a .NET-only repository, **prefer the Roslyn engine** — its namespace-aware
pairing beats the polyglot engine's identifier overlap.

## When to Use

- User asks "where should I add tests?", "which files have no tests?", "find
  untested code", "give me a test gap list", "what's the next file to test".
- Before invoking a test-generation agent, to produce a prioritized worklist.
- After generating tests, to verify each new test file pairs to a source file.
- To enumerate "weakly paired" source files (only one referring test) for
  follow-up depth checks.

## When Not to Use

- **Line/branch coverage** — use `coverage-analysis`.
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

# Save the report
dotnet run scripts/Find-UntestedSources.cs -- <repo-root> > pairing.json

# Iterate the untested list, highest-API-surface first
$report = Get-Content pairing.json | ConvertFrom-Json
$report.untested | Select-Object -First 10 source, decl_count, suggested_test_path
```

Diagnostics go to stderr; JSON goes to stdout.

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
   | Rust | path contains `tests/`/`benches/`. |
   | C# | path contains `tests/`; or project segment ends `.Tests`/`.Test`/`.UnitTests`/`.IntegrationTests`; or filename ends `Tests`/`Test`. |
   | Ruby | path contains `spec/`/`test/`; or filename ends `_spec.rb`/`_test.rb`. |

4. **Per-file extraction** — `process(text, ProcessConfig(structure, imports,
   symbols))` returns declared items, raw import statements, and a flat declared
   -name list.
5. **Pairing** — for each test file, union **import resolution** (per language,
   e.g. Python `from pkg.mod import x` → `pkg/mod.py`; Java `import a.b.C;` →
   `a/b/C.java`; C# `using` is namespace-not-file, so a no-op) with **identifier
   overlap** (word-like tokens, length ≥ 4, matched against declared names).
6. **JSON emit** — `untested_sources` ordered by declaration count descending.

## Limitations (be honest with the agent)

Both engines are static, parse-only heuristics that trade a little accuracy for
orders-of-magnitude lower cost than coverage. Known gaps:

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

For these cases, run actual coverage (`coverage-analysis`) on the unpaired
candidates the agent has already triaged.

## Outputs the agent should consume

- `untested[*].source` / `untested_sources[*].path` — pick the next source file
  to test (highest declaration count first).
- `*.suggested_test_path` — drop-in target for the new test file; the Roslyn
  engine honors the test project that already `<ProjectReference>`s the source's
  project, so `dotnet sln add` is not needed.
- `source_to_tests` (Roslyn) / `--include-tested` `tested_sources` (polyglot) —
  verify a newly written test file lands in the list for the intended source.
- `orphan_tests` (polyglot) — tests that don't reference any same-language
  source file; useful for triaging stale or integration-only tests.
