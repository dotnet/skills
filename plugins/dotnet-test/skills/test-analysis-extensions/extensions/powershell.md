# PowerShell Test Framework Reference (Pester v5)

Reference data for analyzing PowerShell test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — `*.Tests.ps1`, `Describe`/`Context`/`It` |
| Assertion detection | Strong — `Should -Be*`, `-Throw`, `-HaveCount` |
| Sleep/delay detection | Strong — `Start-Sleep` |
| Skip/ignore detection | Strong — `-Skip`, `-Pending`, `Set-ItResult -Skipped` |
| Setup/teardown detection | Strong — `BeforeEach`, `AfterAll`, etc. |
| Tag support | **auto-edit** — `-Tag` parameter on `Describe`/`Context`/`It` |

## Test File Identification

| Convention | Description |
|------------|-------------|
| `*.Tests.ps1` | Standard Pester test file convention |
| `Describe '...' { ... }` | Top-level test group |
| `Context '...' { ... }` | Sub-group |
| `It 'should ...' { ... }` | Individual test case |
| `InModuleScope ModuleName { ... }` | Access internal functions of a module |

Pester v5+ uses block-scoped variables — `Describe`/`Context` blocks run during discovery; `BeforeAll` is required to initialize variables used by `It` blocks.

## Assertion APIs

| Category | Pester v5 (`Should`) |
|----------|----------------------|
| Equality | `$x \| Should -Be $y` |
| Strict equality | `$x \| Should -BeExactly $y` (case-sensitive for strings) |
| Inequality | `$x \| Should -Not -Be $y` |
| Boolean true/false | `$x \| Should -BeTrue` / `Should -BeFalse` |
| Null | `$x \| Should -BeNullOrEmpty` |
| Exception | `{ Get-Item missing } \| Should -Throw` / `Should -Throw -ExpectedMessage "*pattern*"` / `Should -Throw -ErrorId "ItemNotFound,..."` |
| Type | `$x \| Should -BeOfType [int]` |
| String contains | `$s \| Should -Match 'regex'` / `Should -BeLike 'wild*'` |
| Collection | `$arr \| Should -Contain $item` / `Should -HaveCount 3` |
| File exists | `'path' \| Should -Exist` |
| Mocks | `Should -Invoke Get-Item -Times 1 -Exactly` / `Should -Invoke -ParameterFilter { $Path -eq '/x' }` |
| Negation | `Should -Not -Be`, `Should -Not -Throw`, `Should -Not -BeNullOrEmpty` |

`Should -Invoke` counts as a state/side-effect assertion — do not flag tests that only verify mock calls as assertion-free.

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Sleep | `Start-Sleep -Seconds 5` |
| Sleep ms | `Start-Sleep -Milliseconds 500` |
| Wait-Job | `Wait-Job -Job $job -Timeout 10` (acceptable for legitimate job waits) |
| Loop wait | `while (-not (Test-Ready)) { Start-Sleep -Seconds 1 }` |

## Skip/Ignore Annotations

| Mechanism | Example |
|-----------|---------|
| `-Skip` | `It 'does x' -Skip { ... }` |
| `-Pending` | `It 'does x' -Pending { ... }` (legacy v4; in v5, prefer `-Skip`) |
| `Set-ItResult -Skipped -Because '<reason>'` | Inline skip from within an `It` body |
| Conditional skip | `It 'is windows-only' -Skip:(-not $IsWindows) { ... }` |
| `-Skip` on `Describe`/`Context` | skips all contained tests |

## Exception Handling — Idiomatic Alternatives

```powershell
# Preferred: Should -Throw with scriptblock
{ Get-Item -Path 'C:\nonexistent' -ErrorAction Stop } |
    Should -Throw -ErrorId 'PathNotFound,Microsoft.PowerShell.Commands.GetItemCommand'

# With pattern match on message:
{ Invoke-MyFunc -InvalidArg } | Should -Throw -ExpectedMessage '*invalid*'

# Not throwing:
{ Invoke-MyFunc -ValidArg } | Should -Not -Throw
```

Flag tests using `try { ... } catch { Write-Error ... }` patterns without subsequent `Should` assertion.

## Mystery Guest — Common PowerShell Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `Get-Content 'C:\hard\coded\path'`, `Test-Path` against real paths, `New-Item` without `TestDrive:` |
| Registry | `Get-ItemProperty 'HKLM:\...'`, `Set-ItemProperty` against real registry |
| Network | `Invoke-WebRequest`, `Invoke-RestMethod` against real URLs |
| Environment | `$env:USERNAME`, `$env:COMPUTERNAME` (without mock or fallback) |
| Acceptable | `TestDrive:` (Pester-provided per-test temp dir), `Mock` cmdlet, hashtables as fake config |

## Integration Test Markers

- File suffix: `*.Integration.Tests.ps1`, `*.E2E.Tests.ps1`
- `-Tag 'Integration'` / `-Tag 'E2E'`
- Folder convention: `tests/integration/`, `tests/e2e/`
- Real Azure/AWS module calls (`Connect-AzAccount`, `Get-S3Object`) imply integration

## Setup/Teardown

| Scope | Setup | Teardown |
|-------|-------|----------|
| Per-test | `BeforeEach { }` | `AfterEach { }` |
| Per-block (Describe/Context) | `BeforeAll { }` | `AfterAll { }` |

Pester v5 requires `BeforeAll` to initialize variables used in `It` blocks (discovery vs run separation). A common mistake: defining variables at `Describe` scope and using them inside `It` — they will be `$null` at run time.

## Tag/Trait Attributes (for `test-tagging`)

| Mechanism | Example |
|-----------|---------|
| `-Tag` on `It` | `It 'creates order' -Tag 'positive','critical-path' { ... }` |
| `-Tag` on `Context` | inherits to contained `It`s |
| `-Tag` on `Describe` | inherits to all nested blocks |
| `Invoke-Pester -Tag 'positive' -ExcludeTag 'slow'` | filter by tag |

## Language-specific calibration notes

- **Pester v5 vs v4 scoping differences**: v4 tests using `$script:` variables shared between `It` blocks won't work in v5 the same way. Note as migration debt if both styles coexist.
- **`InModuleScope`** is the canonical way to test internal/non-exported module functions — not an implementation-coupling smell.
- **`Mock` cmdlet** intercepts ANY function in scope; tests that mock built-in cmdlets (`Get-ChildItem`, etc.) without `ParameterFilter` are over-broad — flag as smell.
- **`TestDrive:`** is an automatically-created temporary directory unique to each test — not a Mystery Guest.
- **Pester `Should -Invoke` (v5) / `Assert-MockCalled` (v4)** are state/side-effect assertions.
- **`Set-StrictMode -Version Latest`** in tests is a hygiene practice — acknowledge as positive.
- **`Set-ItResult -Inconclusive`** marks a test as inconclusive (not failure, not skip).
- **`-ForEach` / `-TestCases`** are parametrized — NOT duplicate tests.
- **PSScriptAnalyzer integration**: tests that lint themselves (`Invoke-ScriptAnalyzer`) are quality-gate tests, not analyzer code.
- **Pester v6 (preview)** changes some APIs; if the project targets v6, double-check assertion forms.
