---
name: find-untested-sources-polyglot
description: >
  Polyglot, parse-only static analysis that pairs source files with
  referencing tests across Python, TypeScript/JavaScript, Go, Java, Rust,
  C#, and Ruby. JSON shape matches `find-untested-sources`.
  USE FOR: where to write tests next, find untested files, list sources
  without tests, polyglot test-pairing map.
  DO NOT USE FOR: coverage, CRAP risk. For .NET-only repos prefer
  `find-untested-sources`.
license: MIT
---

# Find Untested Sources (Polyglot)

## Purpose

Coverage tools answer "which lines were executed?" — they require a green build
and a passing test run, which is minutes-to-tens-of-minutes on a real repo.
The question this skill answers is different and much cheaper:

> _Which source files have no test file referencing any of their declared
> symbols?_

That's the question an agent asks **before** writing a new test — and it can be
answered statically in a few seconds by parsing every recognized source file
with [tree-sitter](https://tree-sitter.github.io/), with **no build, no
dependency resolution, no compilation**.

This is the polyglot sibling of the C# `find-untested-sources` skill. The
output schema is intentionally compatible so the same prompt patterns can
consume either tool.

## When to Use

- The repository is not exclusively C#, or you want a tool that works
  uniformly across multiple languages without per-language plumbing.
- User asks "where should I add tests?", "which files have no tests?", "find
  untested code", "give me a test gap list", "what's the next file to test".
- Before invoking a test-generation agent, to produce a prioritized worklist.
- After generating tests, to verify each new test file pairs to a source file.

## When Not to Use

- **C#-only repo** — prefer `find-untested-sources`. Its Roslyn-based
  namespace disambiguation is strictly better than this skill's identifier
  overlap on duplicated short names like `Settings` or `Context`.
- **Line/branch coverage** — use language-native coverage tooling.
- **Are existing tests strong?** — use `test-gap-analysis` or
  `assertion-quality`.

## Inputs

| Input | Required | Default | Description |
|-------|----------|---------|-------------|
| Repo root | Yes | — | Directory to scan recursively. |
| `--lang LANG` | No | all | Restrict to a language (repeatable). One of `python`, `typescript`, `tsx`, `javascript`, `go`, `java`, `rust`, `csharp`, `ruby`. |
| `--limit-untested N` | No | 0 (no limit) | Truncate the untested list to N entries. |
| `--include-tested` | No | off | Include `tested_sources` in the payload (large). |

### Prerequisites

- Python 3.10+.
- `pip install tree-sitter-language-pack` (single self-contained wheel that
  bundles parsers for 300+ languages and the high-level `process()` API used
  here). No native build, no per-language grammar install.

## Usage

```powershell
# From the skill folder
python scripts/find_untested_sources.py <repo-root>

# Restrict to a language
python scripts/find_untested_sources.py <repo-root> --lang python --lang typescript

# Truncate the report (top 20 by declared API surface)
python scripts/find_untested_sources.py <repo-root> --limit-untested 20 > pairing.json

# Iterate, highest-API-surface first
$report = Get-Content pairing.json | ConvertFrom-Json
$report.untested_sources | Select-Object -First 10 path, declaration_count, suggested_test_path
```

Diagnostics go to stderr; JSON goes to stdout.

## Output Schema

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

Pass `--include-tested` to additionally emit `tested_sources` (same shape as
`untested_sources` but with a `covering_tests` array instead of a suggested
path). Omitted by default to keep the payload small for LLM consumption.

## How It Works

1. **File discovery** — recursive directory walk pruning common build/vendor
   dirs (`bin`, `obj`, `node_modules`, `target`, `dist`, `build`, `vendor`,
   `__pycache__`, `.venv`, `.git`, etc.). Skips generated files (`.d.ts`,
   `.g.cs`, `.Designer.cs`, `_pb2.py`, `*.min.js`, `AssemblyInfo.cs`, ...).

2. **Language detection** — `tree_sitter_language_pack.detect_language_from_path`
   maps the extension to one of the supported languages. Unknown extensions
   are skipped silently.

3. **Test-vs-source classification** — per-language path heuristics:

   | Language | Test rule |
   |---|---|
   | Python | path contains `tests/` or `test/`; or filename starts with `test_` or ends with `_test.py`; or `conftest.py`. |
   | JS/TS/TSX | path contains `__tests__`, `tests`, `test`, `spec`, or `e2e`; or filename contains `.test.` or `.spec.`. |
   | Go | filename ends with `_test.go` (Go's standard convention). |
   | Java | path contains `test` or `tests`; or filename ends with `Test.java` / `Tests.java`. |
   | Rust | path contains `tests/` or `benches/`. |
   | C# | path contains `tests/`; or project segment ends with `.Tests`, `.Test`, `.UnitTests`, `.IntegrationTests`; or filename ends with `Tests`/`Test`. |
   | Ruby | path contains `spec/`, `test/`; or filename ends with `_spec.rb` / `_test.rb`. |

4. **Per-file extraction** — `tree_sitter_language_pack.process(text,
   ProcessConfig(language=lang, structure=True, imports=True, symbols=True))`
   returns:
   - `structure` — top-level declared items (functions, classes, methods,
     traits, ...) with their names. Used as the declared-symbol set.
   - `imports` — raw import statements (e.g. `from foo import bar`,
     `import "pkg/util"`, `using System.IO;`, `use crate::foo::Bar;`).
   - `symbols` — flat declared-name list, unioned with `structure` (acts as
     a fallback when `structure` is empty, and broadens coverage when both
     are populated; declaration counts may exceed pure structure parsing).

5. **Pairing** — for each test file, union the results of:
   - **Import resolution** (per language):
     - Python: `from pkg.mod import x` → `pkg/mod.py` or
       `pkg/mod/__init__.py`.
     - TS/JS: relative `./foo` / `../bar` → with `.ts`/`.tsx`/`.js`/`.jsx`
       and `/index.*` candidate paths.
     - Go: `"path/to/pkg"` → any source file whose final path segment
       matches `pkg.go` in the index.
     - Java: `import a.b.C;` → `a/b/C.java`.
     - Rust: `use a::b::C;` → `b.rs` or `C.rs` (best-effort, no module tree).
     - Ruby: `require 'foo/bar'` → `foo/bar.rb`.
     - C#: `using` maps to namespaces, not files; intentionally a no-op —
       falls through to identifier overlap below.
   - **Identifier overlap** — every word-like token in the test source is
     looked up in the source index of declared names (length ≥ 4 to keep
     noise down). Any source whose declared name appears as a token in the
     same-language test is paired.

6. **JSON emit** — `untested_sources` is ordered by declaration count
   descending so the highest-API-surface gap appears first.

## Limitations

This is a static, parse-only heuristic. It deliberately trades a small amount
of accuracy for orders-of-magnitude lower cost than coverage. Known gaps:

- **Reflection / DI-resolved types** that a test only references through a
  string name or container resolution don't appear in the identifier scan.
- **C#** specifically: namespace disambiguation is the C# tool's strength;
  this polyglot version intentionally skips it. If you have a .NET-only
  repository, prefer the Roslyn-based `find-untested-sources`.
- **Short identifier names** (< 4 chars) are dropped from the overlap index
  to avoid noisy pairings on names like `id`, `db`, `Tag`.
- **Cross-language tests** (Python tests driving a Go binary, etc.) are
  recorded as orphan tests since same-language pairing is the rule.
- **Monorepo path aliases** (TS path mapping, Java module-info) are not
  resolved; the suffix-match fallback may pick the wrong source if two files
  share a trailing path segment in different sub-projects.

For these cases, run actual coverage on the unpaired candidates the agent
has already triaged.

## Outputs the agent should consume

- `untested_sources[*].path` — pick the next source file to test (highest
  `declaration_count` first).
- `untested_sources[*].suggested_test_path` — drop-in target for the new
  test file using the per-language convention.
- (With `--include-tested`) `tested_sources[*].covering_tests` — verify a
  newly written test file lands in the list for the intended source.
- `orphan_tests` — tests that don't appear to reference any same-language
  source file; useful for triaging stale tests or integration-only tests.
