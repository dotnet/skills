# PowerShell Extension

Language-specific guidance for PowerShell test generation using Pester v5.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — find `*.Tests.ps1` files and copy their style (structure, assertions, mock approach, import method)
2. **Module structure** — look for `.psd1` (manifest), `.psm1` (root module), `Public/`/`Private/` organization
3. **Build/test scripts** — check for `build.ps1`, `Invoke-Build` (`*.build.ps1`), `psake`, or CI scripts
4. **Shell target** — check `.psd1` for `PowerShellVersion`/`CompatiblePSEditions`, CI matrix for `pwsh` vs `powershell.exe`

Use the repo's existing test conventions. Only add Pester if the repo has no tests at all.

## Build Commands

PowerShell is interpreted — no build step. If the repo has a build script, use it. Otherwise validate with:

- **Module loads**: `Import-Module ./MyModule.psd1 -Force -ErrorAction Stop`
- **Script analyzer**: `Invoke-ScriptAnalyzer -Path ./src -Recurse` (if PSScriptAnalyzer is available)
- **Lint**: `Invoke-ScriptAnalyzer -Path path/to/file.ps1 -Fix`

## Test Commands

| Scope | Command |
|-------|---------|
| All tests | `Invoke-Pester` |
| Specific file | `Invoke-Pester -Path ./Tests/Get-Widget.Tests.ps1` |
| Filter by name | `Invoke-Pester -FullNameFilter '*Get-Widget*'` |
| Filter by tag | `Invoke-Pester -TagFilter 'Unit'` |
| Non-interactive (CI) | `Invoke-Pester -CI` |
| Detailed output | `Invoke-Pester -Output Detailed` |

- Prefer the repo's build/test script over raw `Invoke-Pester`
- Use `-Output Detailed` during fix cycles, `-Output Minimal` for final validation

## Project Layout and Imports

| Layout | Import in `BeforeAll` |
|--------|-----------------------|
| Module (`.psd1`) | `Import-Module "$PSScriptRoot/../MyModule.psd1" -Force` |
| Library script (defines functions) | `. $PSScriptRoot/Get-Widget.ps1` |
| Co-located test | `. $PSCommandPath.Replace('.Tests.ps1', '.ps1')` |
| Executable script (has `param()`) | Do **not** dot-source — invoke with `& $PSScriptRoot/script.ps1 -Param value` and assert on output/errors |

- **All imports go in `BeforeAll`** — never at script top level
- **Use `$PSScriptRoot` or `$PSCommandPath`** — never `$MyInvocation.MyCommand.Path` (returns empty in `BeforeAll`)
- Use `-Force` on `Import-Module` to pick up changes between runs

## Test File Naming

- Files: `*.Tests.ps1` — match existing convention (co-located vs `Tests/` directory)

## Pester v5 Discovery vs Run (Critical)

Pester v5 runs in **two phases**: Discovery (collects test metadata) then Run (executes tests). This is the #1 source of agent errors.

**Rules:**
- All setup code goes in `BeforeAll` or `BeforeEach` — never at script top level or loose inside `Describe`/`Context`
- Code directly inside `Describe`/`Context` (but outside `It`/`Before*`/`After*`) runs during **Discovery** — do not put setup, imports, or variable assignments there
- Data for `-ForEach` / `-TestCases` must be set in `BeforeDiscovery`, not `BeforeAll` (BeforeAll runs after discovery)
- `-Skip:$condition` evaluates at Discovery time — conditions from `BeforeAll` will be `$null`
- Use `foreach` loops for dynamic test generation only with `BeforeDiscovery` data
- Use `TestDrive:` for file-based tests instead of touching repo files — Pester cleans it up automatically

## Common Errors

| Error | Fix |
|-------|-----|
| Variable is `$null` in `It` block | Move assignment into `BeforeAll` — variables set there are visible to child `It` blocks without `$script:` |
| `-ForEach` data is empty | Move data setup from `BeforeAll` to `BeforeDiscovery` |
| `CommandNotFoundException` for Mock target | The function must exist before mocking — import the module in `BeforeAll` first |
| `$MyInvocation.MyCommand.Path` returns empty | Use `$PSCommandPath` or `$PSScriptRoot` instead |
| `Should Be` (no dash) fails | Use v5 syntax: `Should -Be` (with dash prefix) |
| `Assert-MockCalled` not recognized | Use v5 syntax: `Should -Invoke` |
| Mock has no effect | Check scope — mocks in `It` only apply to that `It`; use `BeforeAll`/`BeforeEach` for broader scope |
| `Should -Throw` doesn't catch cmdlet errors | Most cmdlet errors are non-terminating — wrap with `{ cmd -ErrorAction Stop }` or set `$ErrorActionPreference = 'Stop'` in `BeforeEach` |
| Tests pass on Windows but fail on Linux | Use `Join-Path` not string concatenation; match exact file casing; avoid Windows-only cmdlets (Registry, EventLog) |

## Mocking Rules

- Place mocks in `BeforeAll` (shared) or `BeforeEach` (reset per test)
- Mock where the command is **called from** — use `-ModuleName` to mock inside a module's scope
- Use `-ParameterFilter` for selective mocking (no `param()` block needed in v5)
- Verify calls with `Should -Invoke` — default scope inside `It` counts only that test's calls
- Use `InModuleScope` sparingly and as narrowly as possible — prefer `Mock -ModuleName` for testing via public API
- Inside mock bodies, use `$PesterBoundParameters` not `$PSBoundParameters`
- If a test needs more than 3 mocks, flag it as a design smell

## Non-Obvious Assertions

Most `Should` operators are self-explanatory. These are the ones agents get wrong:

- `Should -Throw` requires a **scriptblock**: `{ risky-op } | Should -Throw` — not a direct call
- `Should -Contain` is for **collections** — use `Should -Be` for scalar equality
- `Should -HaveParameter` validates cmdlet signatures: `Get-Command X | Should -HaveParameter 'Name' -Mandatory`
- `Should -Invoke` verifies mock calls: `Should -Invoke Get-Item -Times 1 -Exactly`

## Cross-Platform

- Prefer `pwsh` (PowerShell 7+) unless the repo explicitly targets Windows PowerShell 5.1
- Use `Join-Path` for paths — never string concatenation with `\`
- Linux/macOS file systems are **case-sensitive** — match exact casing in imports and paths
- Windows ships Pester 3.4.0 — if v5 is needed: `Install-Module Pester -Force -SkipPublisherCheck`
- Check `$PSVersionTable.PSEdition` to detect Core vs Desktop

## Skip Coverage Tools

Do not configure or run coverage tools (Pester CodeCoverage, JaCoCo export). Coverage is measured separately by the evaluation harness.
