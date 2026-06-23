# dotnet-test-migration

Skills and an orchestrator agent for migrating .NET test frameworks and platforms. Covers framework version upgrades (MSTest, xUnit), cross-framework conversion (xUnit → MSTest), and test platform migration (VSTest → Microsoft.Testing.Platform).

## When to use this plugin

- **Upgrade MSTest** — MSTest v1/v2 → v3, then v3 → v4 (handles source and behavioral breaking changes)
- **Upgrade xUnit** — xUnit.net v2 → v3
- **Convert xUnit to MSTest** — port xUnit (v2 or v3) projects to MSTest v4
- **Adopt Microsoft.Testing.Platform** — migrate from the VSTest runner to MTP
- **Orchestrate migrations** — auto-detect the current framework/version/platform and route to the right migration

## Skills

| Skill | Description |
|---|---|
| **migrate-mstest-v1v2-to-v3** | Upgrade MSTest v1 (assembly refs) or v2 (NuGet 1.x–2.x) to v3 |
| **migrate-mstest-v3-to-v4** | Upgrade MSTest v3 to v4 — handles all source and behavioral breaking changes |
| **migrate-xunit-to-xunit-v3** | Upgrade xUnit.net v2 to v3 |
| **migrate-xunit-to-mstest** | Convert xUnit.net (v2 or v3) test projects to MSTest v4 — attributes, assertions, fixtures, lifecycle, output, parallelization |
| **migrate-vstest-to-mtp** | Migrate from the VSTest runner to Microsoft.Testing.Platform |

## Agents

| Agent | Purpose |
|---|---|
| **test-migration** | Auto-detects framework/version/platform and routes to the correct migration skill; coordinates multi-step migrations (e.g., MSTest v1 → v3 → v4) |

## Related plugins

- **dotnet-test** — running, generating, and analyzing tests; testability improvement; coverage. The migration skills here reference shared `dotnet-test` skills by name (e.g., `platform-detection` for framework/platform detection, `writing-mstest-tests` for idiomatic MSTest polish, and `run-tests` for verification). Install `dotnet-test` alongside this plugin to get the full workflow.

## Prerequisites

- .NET SDK installed (`dotnet` on PATH)
- A project with an existing test framework (MSTest, xUnit, NUnit, or TUnit)
