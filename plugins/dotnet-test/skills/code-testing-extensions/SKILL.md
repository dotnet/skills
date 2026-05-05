---
name: code-testing-extensions
description: >-
  Provides file paths to language-specific extension files for the code-testing
  pipeline. Call this skill to discover available extension guidance files
  (e.g., dotnet.md for .NET, cpp.md for C++). Do not use directly — invoked
  by code-testing agents and skills that need language-specific references.
user-invocable: false
license: MIT
---

# Code Testing Extensions

This skill provides access to language-specific guidance files used by the code-testing pipeline. Call this skill to get the file paths, then read the relevant file for your target language.

## Available Extension Files

| File | Language | Contents |
|------|----------|----------|
| [extensions/dotnet.md](extensions/dotnet.md) | .NET (C#/F#/VB) | Build commands, test commands, project reference validation, common CS error codes, MSTest template |
| [extensions/cpp.md](extensions/cpp.md) | C++ | Testing internals with friend declarations |
| [extensions/dotnet-examples.md](extensions/dotnet-examples.md) | .NET (C#/F#/VB) | Concrete pipeline examples: sample research output, plan, generated tests, fix cycles, final report |

## Usage

Read the appropriate extension file for the target language before writing test code.
