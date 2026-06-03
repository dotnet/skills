---
name: test-analysis-extensions
description: >-
  Provides file paths to language-specific reference files for the test
  ANALYSIS skills (assertion-quality, test-anti-patterns, test-gap-analysis,
  test-smell-detection, test-tagging). Call this skill to discover available
  extension files (e.g., dotnet.md for .NET/MSTest/xUnit/NUnit/TUnit,
  python.md for pytest/unittest, typescript.md for Jest/Vitest/Mocha,
  java.md for JUnit/TestNG, etc.). Do not use directly — invoked by the
  test-quality-auditor agent and polyglot analysis skills that need
  framework-specific lookup tables (test markers, assertion APIs, skip
  annotations, sleep patterns, mystery guest indicators, integration
  markers, setup/teardown, tag-support capability).
user-invocable: false
license: MIT
---

# Test Analysis Extensions

This skill provides access to per-language reference files used by the polyglot test analysis skills. Call this skill to get the list of available extension files, then read the one matching the target codebase's language and test framework.

## Available Extension Files

| File | Languages / Frameworks | Contents |
|------|------------------------|----------|
| [extensions/dotnet.md](extensions/dotnet.md) | .NET (C#/F#/VB) — MSTest, xUnit, NUnit, TUnit | Test markers, assertion APIs, sleep/delay patterns, skip annotations, mystery guest, integration markers, setup/teardown, tag support |
| [extensions/python.md](extensions/python.md) | Python — pytest, unittest | Same categories, with pytest fixtures/markers and unittest TestCase |
| [extensions/typescript.md](extensions/typescript.md) | TypeScript / JavaScript — Jest, Vitest, Mocha, Jasmine, node:test | Same categories, with async/await pitfalls |
| [extensions/java.md](extensions/java.md) | Java — JUnit 4, JUnit 5 (Jupiter), TestNG | Same categories, with `@Tag` / `@Category` / groups |
| [extensions/go.md](extensions/go.md) | Go — `testing` package, testify | Same categories, with table-driven idiom and build tags |
| [extensions/ruby.md](extensions/ruby.md) | Ruby — RSpec, Minitest | Same categories, with RSpec metadata and Minitest tags |
| [extensions/rust.md](extensions/rust.md) | Rust — built-in `#[test]`, `cargo test` | Same categories, with `#[ignore]`, `#[should_panic]`, feature flags |
| [extensions/swift.md](extensions/swift.md) | Swift — XCTest, Swift Testing | Same categories, with `@Test`, `@Tag`, `@Suite` |
| [extensions/kotlin.md](extensions/kotlin.md) | Kotlin — JUnit 5, Kotest, MockK | Same categories, with `@Tag` and Kotest tags |
| [extensions/powershell.md](extensions/powershell.md) | PowerShell — Pester v5 | Same categories, with `-Tag` and `Skip` |
| [extensions/cpp.md](extensions/cpp.md) | C++ — GoogleTest, Catch2, doctest | Same categories, with `[tags]` and `*` filters |

## Usage

1. Detect the target codebase's primary language and test framework.
2. Read the matching extension file before performing analysis.
3. If multiple test frameworks are present (e.g., a project mixing Jest and Mocha), read all relevant extensions.
4. Each extension file documents the same categories so analysis skills can be language-neutral.

## Capability tags

Each extension file declares per-capability support so skills can gate behaviour safely:

- **Test discovery** — how to locate test files and methods.
- **Assertion detection** — framework-specific and language-level assertion forms.
- **Sleep/delay patterns** — synchronous and asynchronous waits.
- **Skip / ignore** — how to recognize skipped/ignored tests.
- **Setup / teardown** — fixture and lifecycle hooks.
- **Mystery guest indicators** — common file/db/network/env coupling patterns.
- **Integration markers** — conventions that mark a test as integration/E2E.
- **Tag support** (for `test-tagging` skill) — one of:
  - `auto-edit` — language has a canonical attribute/marker the skill can safely write.
  - `report-only` — no canonical syntax; produce audit reports without edits.
  - `convention-based` — tags exist via name/comment conventions only.

## Notes for skill authors

- Treat extension files as data, not as guidance to follow verbatim. They tell skills *how to detect things* in each language, not *what to think* about findings.
- When language detection is uncertain, prefer reading multiple extension files over guessing.
- If the user explicitly names a framework that does not have an extension file yet, fall back to the closest one (e.g., Pest → python.md/pytest semantics) and note the gap in the report.
