---
name: code-testing-extensions
description: >-
  Provides file paths to language-specific extension files for the code-testing
  pipeline. Call this skill to discover available extension guidance files
  (e.g., dotnet.md for .NET, cpp.md for C++). Do not use directly — invoked
  by code-testing agents and skills that need language-specific references.
user-invocable: false
disable-model-invocation: true
license: MIT
---

# Code Testing Extensions

This skill provides access to language-specific guidance files used by the code-testing pipeline. Call this skill to get the file paths, then read the relevant file for your target language.

## Available Extension Files

| File | Language | Contents |
|------|----------|----------|
| [extensions/dotnet.md](extensions/dotnet.md) | .NET (C#/F#/VB) | Build commands, test commands, project reference validation, common CS error codes, MSTest template |
| [extensions/python.md](extensions/python.md) | Python | Framework-adaptive test commands (pytest, custom runners), project layout detection, mocking guidelines, common errors |
| [extensions/typescript.md](extensions/typescript.md) | TypeScript/JavaScript | Build/test commands (Jest/Vitest/Mocha), framework detection, mocking, TS-specific considerations |
| [extensions/powershell.md](extensions/powershell.md) | PowerShell | Test commands (Pester v5), module import patterns, discovery/run pitfalls, mocking, common errors |
| [extensions/cpp.md](extensions/cpp.md) | C++ | Testing internals with friend declarations |
| [extensions/go.md](extensions/go.md) | Go | `go test` commands, table-driven tests, integration vs unit layout, mocking via interfaces, common errors |
| [extensions/java.md](extensions/java.md) | Java | Maven/Gradle commands, JUnit 4/5 and TestNG detection, Mockito, Spring Boot slices, common errors |
| [extensions/rust.md](extensions/rust.md) | Rust | `cargo test` commands, unit vs integration vs doc tests, features, async test harnesses, common errors |
| [extensions/ruby.md](extensions/ruby.md) | Ruby | RSpec and Minitest commands, Bundler usage, Rails specifics, mocking patterns, common errors |
| [extensions/swift.md](extensions/swift.md) | Swift | SPM and Xcode test commands, XCTest vs Swift Testing, `@testable import`, async/throws tests, common errors |
| [extensions/kotlin.md](extensions/kotlin.md) | Kotlin | Gradle commands, JUnit/Kotest detection, MockK, coroutines test, KMP and Android specifics, common errors |
| [extensions/dotnet-examples.md](extensions/dotnet-examples.md) | .NET (C#/F#/VB) | Concrete pipeline examples: sample research output, plan, generated tests, fix cycles, final report |
| [extensions/python-examples.md](extensions/python-examples.md) | Python | Concrete pipeline examples (pytest): research, plan, generated test file, fix cycles, final report |
| [extensions/typescript-examples.md](extensions/typescript-examples.md) | TypeScript/JavaScript | Concrete pipeline examples (Vitest, applicable to Jest): research, plan, generated test file, fix cycles, final report |
| [extensions/go-examples.md](extensions/go-examples.md) | Go | Concrete pipeline examples (standard `testing`): research, plan, table-driven test file, fix cycles, final report |
| [extensions/java-examples.md](extensions/java-examples.md) | Java | Concrete pipeline examples (JUnit 5 + Mockito on Maven): research, plan, generated test file, fix cycles, final report |

## Usage

Read the appropriate base extension before writing test code. Load an
`<language>-examples.md` file only when the repository has no representative
tests and the base extension does not resolve the needed pattern. Do not load
end-to-end examples for a focused request with established local conventions.
