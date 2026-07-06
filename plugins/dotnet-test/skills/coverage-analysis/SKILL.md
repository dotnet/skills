---
name: coverage-analysis
description: >
  Project-wide code coverage and CRAP (Change Risk Anti-Patterns) score
  analysis for .NET projects. Calculates CRAP scores per method and surfaces
  risk hotspots — complex code with low coverage that is dangerous to modify.
  Use to diagnose why coverage is stuck or plateaued, identify what methods
  block improvement, or get project-wide coverage analysis with risk ranking.
  USE FOR: coverage stuck, coverage plateau, can't increase coverage, what's
  blocking coverage, coverage gap, CRAP scores, risk hotspots, where to add
  tests, coverage analysis, coverage report.
  DO NOT USE FOR: targeted single-method CRAP analysis (use crap-score);
  auditing test code for coverage-touching or other anti-patterns (use
  test-anti-patterns); writing tests; running tests (use run-tests). Requires
  or produces coverage (Cobertura) and CRAP metrics.
license: MIT
---

# Coverage Analysis

## Purpose

Raw coverage percentages answer "what code was executed?" — they don't answer what you actually need to know:

- **What tests should I write next?** — ranked by risk and impact
- **Which uncovered code is risky vs. trivial?** — CRAP scores separate the two
- **Why has coverage plateaued?** — identify the files blocking further gains
- **Is this code safe to refactor?** — complex + uncovered = dangerous to change

This skill bridges that gap: from a bare .NET solution to a prioritized risk hotspot list, with no manual tool configuration required.

## When to Use

Use this skill when the user mentions test coverage, coverage gaps, code risk, CRAP scores, where to add tests, why coverage plateaued, or wants to know which code is safest to refactor — even if they don't explicitly say "coverage analysis".

## When Not to Use

- **Targeted single-method CRAP analysis** — use the `crap-score` skill instead
- **Writing or generating tests** — this skill identifies where tests are needed, not write them
- **General test execution** unrelated to coverage or CRAP analysis
- **Coverage reporting without CRAP context** — use `dotnet test` with coverage collection directly

## Inputs

| Input | Required | Default | Description |
|-------|----------|---------|-------------|
| Project/solution path | No | Current directory | Path to the .NET solution or project |
| Line coverage threshold | No | 80% | Minimum acceptable line coverage |
| Branch coverage threshold | No | 70% | Minimum acceptable branch coverage |
| CRAP threshold | No | 30 | Maximum acceptable CRAP score before flagging |
| Top N hotspots | No | 10 | Number of risk hotspots to surface |

### Prerequisites

- .NET SDK installed (`dotnet` on PATH)
- At least one test project referencing the production code (xUnit, NUnit, or MSTest) — only required for the from-scratch path; not needed when the user supplies an existing Cobertura XML
- **Optional, only for the from-scratch path:** internet/NuGet access for `dotnet add package coverlet.collector` (or `Microsoft.Testing.Extensions.CodeCoverage`) when a test project has no coverage provider yet. Skip when the user supplies an existing Cobertura XML.
- **Optional, only for Phase 5:** internet access for `dotnet tool install` (ReportGenerator). Core CRAP/coverage analysis works from Cobertura XML alone — ReportGenerator only adds HTML/CSV reports as an optional post-summary extra.

The skill auto-detects coverage provider state per test project and selects the least-invasive execution strategy:

- unified Microsoft CodeCoverage when all projects use it,
- unified Coverlet when no project uses Microsoft CodeCoverage,
- per-project provider execution when the solution is truly mixed.

No pre-existing runsettings files or manually installed tools required.

## Workflow

> **MANDATORY: deliver the final assistant response with the CRAP/risk-hotspot summary BEFORE any optional work.** As soon as `Compute-CrapScores.ps1` and `Extract-MethodCoverage.ps1` return data, your **next** assistant response must contain the user-facing analysis (CRAP table, blocking methods, recommendations). Do not run ReportGenerator (Phase 5), do not install global tools, and do not start any heavy parallel work before that response is delivered. The user is judged on the final assistant message, not on side-effect files.
>
> If a phase fails, times out, or budget is running low, skip remaining optional work and immediately return a partial summary containing: (1) what was found in the Cobertura XML, (2) any CRAP/risk-hotspot data already extracted, (3) which methods are blocking coverage, and (4) failures encountered.

If the user provides a path to existing Cobertura XML (or coverage data is already present in `TestResults/`), **skip Phase 2 entirely** (no test execution) **and skip Phase 5 by default** (no ReportGenerator install or HTML report) — go directly from Phase 3 (analysis scripts) to Phase 4 (user-facing summary). Only run Phase 5 if the user explicitly asks for HTML/CSV reports. The Risk Hotspots table and CRAP scores are mandatory in every output — they are the skill's core value-add over raw coverage numbers.

The workflow runs in five phases. Phases 1–4 are required; Phase 5 (ReportGenerator HTML/CSV reports) is strictly optional and runs **after** the user-facing summary has been delivered. Do not parallelize Phase 5 with earlier phases — the heavy `dotnet tool install` for ReportGenerator can crash the session before Phase 4 completes.

### Phase 1 — Setup (sequential)

#### Step 1: Locate the solution or project

Given the user's path (default: current directory), find the entry point:

```powershell
$root = "<user-provided-path-or-current-directory>"

# Prefer solution file; fall back to project file
$sln = Get-ChildItem -Path $root -Filter "*.sln" -Recurse -Depth 2 -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($sln) {
    Write-Host "ENTRY_TYPE:Solution"; Write-Host "ENTRY:$($sln.FullName)"
} else {
    $project = Get-ChildItem -Path $root -Filter "*.csproj" -Recurse -Depth 2 -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($project) {
        Write-Host "ENTRY_TYPE:Project"; Write-Host "ENTRY:$($project.FullName)"
    } else {
        Write-Host "ENTRY_TYPE:NotFound"
    }
}

# Test projects: search path first, then git root, then parent
$searchRoots = @($root)
$gitRoot = (git -C $root rev-parse --show-toplevel 2>$null)
if ($gitRoot) { $gitRoot = [System.IO.Path]::GetFullPath($gitRoot) }
if ($gitRoot -and $gitRoot -ne $root) { $searchRoots += $gitRoot }
$parentPath = Split-Path $root -Parent
if ($parentPath -and $parentPath -ne $root -and $parentPath -ne $gitRoot) { $searchRoots += $parentPath }

$testProjects = @()
foreach ($sr in $searchRoots) {
    # Primary: match by .csproj content (test framework references)
    $testProjects = @(Get-ChildItem -Path $sr -Filter "*.csproj" -Recurse -Depth 5 -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '([/\\]obj[/\\]|[/\\]bin[/\\])' } |
        Where-Object { (Select-String -Path $_.FullName -Pattern 'Microsoft\.NET\.Test\.Sdk|xunit|nunit|MSTest\.TestAdapter|"MSTest"|MSTest\.TestFramework|TUnit' -Quiet) })
    if ($testProjects.Count -gt 0) {
        if ($sr -ne $root) { Write-Host "SEARCHED:$sr" }
        break
    }
}

# Fallback: match by file name convention
if ($testProjects.Count -eq 0) {
    foreach ($sr in $searchRoots) {
        $testProjects = @(Get-ChildItem -Path $sr -Filter "*.csproj" -Recurse -Depth 5 -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)(test|spec)' })
        if ($testProjects.Count -gt 0) {
            if ($sr -ne $root) { Write-Host "SEARCHED:$sr" }
            break
        }
    }
}
Write-Host "TEST_PROJECTS:$($testProjects.Count)"
$testProjects | ForEach-Object { Write-Host "TEST_PROJECT:$($_.FullName)" }

# Resolve the test output root (where coverage-analysis artifacts will be written)
if ($testProjects.Count -eq 0) {
    if ($gitRoot) {
        $testOutputRoot = $gitRoot
    } else {
        $testOutputRoot = $root
    }
} elseif ($testProjects.Count -eq 1) {
    $testOutputRoot = $testProjects[0].DirectoryName
} else {
    # Multiple test projects — find their deepest common parent directory
    $dirs = $testProjects | ForEach-Object { $_.DirectoryName }
    $common = $dirs[0]
    foreach ($d in $dirs[1..($dirs.Count-1)]) {
        $sep = [System.IO.Path]::DirectorySeparatorChar
        while (-not $d.StartsWith("$common$sep", [System.StringComparison]::OrdinalIgnoreCase) -and $d -ne $common) {
            $prevCommon = $common
            $common = Split-Path $common -Parent
            # Terminate if we can no longer move up (at filesystem root or no parent)
            if ([string]::IsNullOrEmpty($common) -or $common -eq $prevCommon) {
                $common = $null
                break
            }
        }
    }
    if ([string]::IsNullOrEmpty($common)) {
        # Fallback when no common parent directory exists (e.g., projects on different drives)
        if ($gitRoot) {
            $testOutputRoot = $gitRoot
        } else {
            $testOutputRoot = $root
        }
    } else {
        $testOutputRoot = $common
    }
}
Write-Host "TEST_OUTPUT_ROOT:$testOutputRoot"
```

- If `ENTRY_TYPE:NotFound` and test projects were found → use the test projects directly as entry points (run `dotnet test` on each test `.csproj`).
- If `ENTRY_TYPE:NotFound` and no test projects found → stop: `No .sln or test projects found under <path>. Provide the path to your .NET solution or project.`
- If `TEST_PROJECTS:0` and `EXISTING_COBERTURA_COUNT` > 0 (Step 2b) → continue with existing Cobertura XML analysis (no `dotnet test` run).
- If `TEST_PROJECTS:0` and `EXISTING_COBERTURA_COUNT` == 0 → stop: `No test projects found (expected projects with 'Test' or 'Spec' in the name), and no existing Cobertura XML was provided. Add a test project or provide a Cobertura file path.`

#### Step 2: Create the output directory

```powershell
$coverageDir = Join-Path $testOutputRoot "TestResults" "coverage-analysis"
if (Test-Path $coverageDir) { Remove-Item $coverageDir -Recurse -Force }
New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null
Write-Host "COVERAGE_DIR:$coverageDir"
```

This step only manages the `TestResults/coverage-analysis/` subdirectory (skill-owned outputs). It must never delete user-supplied Cobertura files — those live one level up at `TestResults/coverage.cobertura.xml` (or wherever the user pointed). If the user provided a path that *is* `TestResults/coverage-analysis/...`, copy the file aside before this step recreates the directory.

#### Step 2b: Discover or accept existing Cobertura XML (required for the existing-data path)

If the user supplied a Cobertura XML path explicitly, use it. Otherwise probe well-known locations and any path the user mentioned:

```powershell
# 1. Honor a user-supplied path first (highest priority)
$coberturaFiles = @()
if ($userSuppliedCoberturaPath -and (Test-Path $userSuppliedCoberturaPath)) {
    $coberturaFiles = @(Get-Item $userSuppliedCoberturaPath)
}

# 2. Otherwise scan TestResults/ at the repo/test root for any *.cobertura.xml
if ($coberturaFiles.Count -eq 0) {
    $searchPaths = @(
        (Join-Path $testOutputRoot "TestResults"),
        (Join-Path $root "TestResults")
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
    foreach ($sp in $searchPaths) {
        $found = @(Get-ChildItem -Path $sp -Filter "*.cobertura.xml" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[/\\]coverage-analysis[/\\]raw[/\\]' })
        if ($found.Count -gt 0) { $coberturaFiles = $found; break }
    }
}

Write-Host "EXISTING_COBERTURA_COUNT:$($coberturaFiles.Count)"
$coberturaFiles | ForEach-Object { Write-Host "EXISTING_COBERTURA:$($_.FullName)" }
```

- If `EXISTING_COBERTURA_COUNT` > 0 → **skip Phase 2 entirely** and pass these paths to the Phase 3 scripts.
- If `EXISTING_COBERTURA_COUNT` == 0 → run Phase 2 to generate fresh coverage; the file paths to feed Phase 3 will be discovered from `<COVERAGE_DIR>/raw/` after `dotnet test`.

#### Step 2c: Recommend ignoring `TestResults/`

```powershell
$pattern = "**/TestResults/"
$gitRoot = (git -C $testOutputRoot rev-parse --show-toplevel 2>$null)
if ($gitRoot) { $gitRoot = [System.IO.Path]::GetFullPath($gitRoot) }
if ($gitRoot) {
    $gitignorePath = Join-Path $gitRoot ".gitignore"
    $alreadyIgnored = $false
    if (Test-Path $gitignorePath) {
        $alreadyIgnored = (Select-String -Path $gitignorePath -Pattern '^\s*(\*\*/)?TestResults/?\s*$' -Quiet)
    }
    if ($alreadyIgnored) {
        Write-Host "GITIGNORE_RECOMMENDATION:already-present"
    } else {
        Write-Host "GITIGNORE_RECOMMENDATION:$pattern"
    }
} else {
    Write-Host "GITIGNORE_RECOMMENDATION:$pattern"
}
```

### Phase 2 — Test execution (skip when Cobertura XML already exists)

Run only when no Cobertura XML is present. If the user already has coverage data, skip directly to Phase 3.

#### Step 3: Detect coverage provider and run `dotnet test` with coverage collection

Before running tests, detect which coverage provider the test projects use. Projects may reference
`Microsoft.Testing.Extensions.CodeCoverage` (Microsoft's built-in provider, common on .NET 9+) or
`coverlet.collector` (open-source, the default in xUnit templates). The provider determines which
`dotnet test` arguments to use — both produce Cobertura XML.

```powershell
# Detect coverage provider per test project
$coverageProvider = "unknown"  # will be set to "ms-codecoverage" or "coverlet"
$msCodeCovProjects = @()
$coverletProjects = @()
$neitherProjects = @()

foreach ($tp in $testProjects) {
    $hasMsCodeCov = Select-String -Path $tp.FullName -Pattern 'Microsoft\.Testing\.Extensions\.CodeCoverage' -Quiet
    $hasCoverlet = Select-String -Path $tp.FullName -Pattern 'coverlet\.collector' -Quiet
    if ($hasMsCodeCov) { $msCodeCovProjects += $tp }
    elseif ($hasCoverlet) { $coverletProjects += $tp }
    else { $neitherProjects += $tp }
}

# Determine the provider strategy
if ($msCodeCovProjects.Count -gt 0 -and $coverletProjects.Count -eq 0) {
    $coverageProvider = "ms-codecoverage"
    Write-Host "COVERAGE_PROVIDER:ms-codecoverage (ms:$($msCodeCovProjects.Count), none:$($neitherProjects.Count))"
} elseif ($coverletProjects.Count -gt 0 -and $msCodeCovProjects.Count -eq 0) {
    $coverageProvider = "coverlet"
    Write-Host "COVERAGE_PROVIDER:coverlet (coverlet:$($coverletProjects.Count), none:$($neitherProjects.Count))"
} elseif ($msCodeCovProjects.Count -gt 0 -and $coverletProjects.Count -gt 0) {
    $coverageProvider = "mixed-project"
    Write-Host "COVERAGE_PROVIDER:mixed-project (ms:$($msCodeCovProjects.Count), coverlet:$($coverletProjects.Count), none:$($neitherProjects.Count))"
} else {
    $coverageProvider = "coverlet"
    Write-Host "COVERAGE_PROVIDER:none-detected — defaulting to coverlet"
}
```

If any discovered test projects have no provider, add one based on the selected strategy:

```powershell
if ($coverageProvider -eq "ms-codecoverage" -and $neitherProjects.Count -gt 0) {
    Write-Host "ADDING_MS_CODECOVERAGE:$($neitherProjects.Count) project(s)"
    foreach ($tp in $neitherProjects) {
        dotnet add $tp.FullName package Microsoft.Testing.Extensions.CodeCoverage --no-restore
        Write-Host "  ADDED_MS_CODECOVERAGE:$($tp.FullName)"
    }
    foreach ($tp in $neitherProjects) {
        dotnet restore $tp.FullName --quiet
    }
}

if (($coverageProvider -eq "coverlet" -or $coverageProvider -eq "mixed-project") -and $neitherProjects.Count -gt 0) {
    Write-Host "ADDING_COVERLET:$($neitherProjects.Count) project(s)"
    foreach ($tp in $neitherProjects) {
        dotnet add $tp.FullName package coverlet.collector --no-restore
        Write-Host "  ADDED:$($tp.FullName)"
    }
    foreach ($tp in $neitherProjects) {
        dotnet restore $tp.FullName --quiet
    }
}
```

Log each addition to the console so the developer sees what changed. Document the additions in the final report (see Output Format).

Run one `dotnet test` per entry point for the selected strategy:

- In `ms-codecoverage` or `coverlet` mode: run a single command for the solution entry (or one per test project if no `.sln` was found).
- In `mixed-project` mode: run one command per test project, using that project's existing provider to avoid dual-provider conflicts.

**Coverlet** (`coverlet.collector`):

```powershell
$rawDir = Join-Path "<COVERAGE_DIR>" "raw"
dotnet test "<ENTRY>" `
    --collect:"XPlat Code Coverage" `
    --results-directory $rawDir `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include="[*]*" `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[*.Tests]*,[*.Test]*,[*Tests]*,[*Test]*,[*.Specs]*,[*.Testing]*" `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.SkipAutoProps=true
```

**Microsoft CodeCoverage** (`Microsoft.Testing.Extensions.CodeCoverage`):

The command syntax depends on the .NET SDK version. In .NET 9, Microsoft.Testing.Platform arguments
must be passed after the `--` separator. In .NET 10+, `--coverage` is a top-level `dotnet test` flag.

```powershell
$rawDir = Join-Path "<COVERAGE_DIR>" "raw"

# Detect SDK version for correct argument placement
$sdkVersion = (dotnet --version 2>$null)
$major = if ($sdkVersion -match '^(\d+)\.') { [int]$Matches[1] } else { 9 }

if ($major -ge 10) {
    # .NET 10+: --coverage is a first-class dotnet test flag
    dotnet test "<ENTRY>" `
        --results-directory $rawDir `
        --coverage `
        --coverage-output-format cobertura `
        --coverage-output $rawDir
} else {
    # .NET 9: pass Microsoft.Testing.Platform arguments after the -- separator
    dotnet test "<ENTRY>" `
        --results-directory $rawDir `
        -- --coverage --coverage-output-format cobertura --coverage-output $rawDir
}
```

**Mixed-project mode** (`Microsoft.Testing.Extensions.CodeCoverage` + `coverlet.collector` in the same solution):

```powershell
$rawDir = Join-Path "<COVERAGE_DIR>" "raw"
$sdkVersion = (dotnet --version 2>$null)
$major = if ($sdkVersion -match '^(\d+)\.') { [int]$Matches[1] } else { 9 }

foreach ($tp in $testProjects) {
    $hasMsCodeCov = Select-String -Path $tp.FullName -Pattern 'Microsoft\.Testing\.Extensions\.CodeCoverage' -Quiet
    if ($hasMsCodeCov) {
        if ($major -ge 10) {
            dotnet test $tp.FullName --results-directory $rawDir --coverage --coverage-output-format cobertura --coverage-output $rawDir
        } else {
            dotnet test $tp.FullName --results-directory $rawDir -- --coverage --coverage-output-format cobertura --coverage-output $rawDir
        }
    } else {
        dotnet test $tp.FullName `
            --collect:"XPlat Code Coverage" `
            --results-directory $rawDir `
            -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura `
            -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include="[*]*" `
            -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[*.Tests]*,[*.Test]*,[*Tests]*,[*Test]*,[*.Specs]*,[*.Testing]*" `
            -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.SkipAutoProps=true
    }
}
```

Exit code handling:

- **0** — all tests passed, coverage collected
- **1** — some tests failed (coverage still collected — proceed with a warning)
- **Other** — build failure; stop and report the error

After the run, locate coverage files:

```powershell
$coberturaFiles = Get-ChildItem -Path (Join-Path "<COVERAGE_DIR>" "raw") -Filter "coverage.cobertura.xml" -Recurse
Write-Host "COBERTURA_COUNT:$($coberturaFiles.Count)"
$coberturaFiles | ForEach-Object { Write-Host "COBERTURA:$($_.FullName)" }
$vsCovFiles = Get-ChildItem -Path (Join-Path "<COVERAGE_DIR>" "raw") -Filter "*.coverage" -Recurse -ErrorAction SilentlyContinue
if ($vsCovFiles) { Write-Host "VS_BINARY_COVERAGE:$($vsCovFiles.Count)" }
```

If `COBERTURA_COUNT` is 0:

- If `VS_BINARY_COVERAGE` > 0: warn the user — *"Found .coverage files (VS binary format) but no Cobertura XML. These were likely produced by Visual Studio's built-in collector, which outputs a binary format by default. This skill needs Cobertura XML. Re-running with the detected provider configured for Cobertura output."* Then re-run the appropriate `dotnet test` command above (Coverlet or Microsoft CodeCoverage) with Cobertura format.
- If no `.coverage` files either: stop and report — *"Coverage files not generated. Ensure `dotnet test` completed successfully and check the build output for errors."*

### Phase 3 — Analysis (sequential)

Run the two bundled PowerShell scripts. Both are cheap and complete in seconds. **Do not** install or invoke ReportGenerator here — that belongs in optional Phase 5, after the user-facing summary has been delivered.

#### Step 4: Calculate CRAP scores using the bundled script

Run `scripts/Compute-CrapScores.ps1` (co-located with this SKILL.md). It reads all Cobertura XML files, applies `CRAP(m) = comp² × (1 − cov)³ + comp` per method, and returns the top-N hotspots as JSON.

To locate the script: find the directory containing this skill's `SKILL.md` file (the skill loader provides this context), then resolve `scripts/Compute-CrapScores.ps1` relative to it. If the script path cannot be determined, calculate CRAP scores inline using the formula below.

```powershell
& "<skill-directory>/scripts/Compute-CrapScores.ps1" `
    -CoberturaPath @(<all COBERTURA file paths as array>) `
    -CrapThreshold <crap_threshold> `
    -TopN <top_n>
```

Script outputs: `OVERALL_LINE_COVERAGE:<n>`, `OVERALL_BRANCH_COVERAGE:<n>` (aggregated project-wide rates across all provided Cobertura files), `TOTAL_METHODS:<n>`, `FLAGGED_METHODS:<n>`, `HOTSPOTS:<json>` (top-N sorted by CrapScore descending). The OVERALL_* values are exactly what the Phase 4 summary needs for the "Line Coverage" / "Branch Coverage" rows — no separate XML parsing tool call is required.

#### Step 5: Extract per-method coverage gaps

Run `scripts/Extract-MethodCoverage.ps1` to get per-method coverage data for the Coverage Gaps table:

```powershell
& "<skill-directory>/scripts/Extract-MethodCoverage.ps1" `
    -CoberturaPath @(<all COBERTURA file paths as array>) `
    -CoverageThreshold <line_threshold> `
    -BranchThreshold <branch_threshold> `
    -Filter below-threshold
```

Script outputs: JSON array of methods below the coverage threshold, sorted by coverage ascending. Use this data to populate the Coverage Gaps by File table in the report.

### Phase 4 — User-facing summary (MANDATORY — your next assistant response)

As soon as Phase 3 completes, **your immediately next assistant response must contain the user-facing analysis** — do not interleave any other tool calls before it. This is the response the user (and any judge) sees. Skipping or deferring this in favor of Phase 5 (ReportGenerator) is a hard failure.

The response must include, at minimum:

1. Overall line and branch coverage — read directly from the `OVERALL_LINE_COVERAGE:` / `OVERALL_BRANCH_COVERAGE:` lines emitted by `Compute-CrapScores.ps1` (no extra Cobertura parsing required)
2. The Risk Hotspots table built from `Compute-CrapScores.ps1` `HOTSPOTS:` output (CRAP scores, complexity, coverage)
3. Identification of the highest-risk method(s) and what is blocking coverage
4. 1–3 prioritized, specific recommendations (which method to test, expected CRAP/coverage impact)

Use `references/output-format.md` verbatim for fixed headings, table structures, symbols, and emoji. Use `references/guidelines.md` for prioritization rules and style.

If Phase 5 has not yet run when you compose this summary, mark the `## 📁 Reports` section's HTML/Text/CSV/GitHub-markdown rows as `Not generated (optional — request HTML reports to enable)`. Only the `coverage-analysis.md` and raw Cobertura paths are guaranteed to exist.

Attempt to save the same content to `TestResults/coverage-analysis/coverage-analysis.md` before delivering the response (use the editor's create/edit tool — do not shell out). If the file write fails, still deliver the summary and note the file-write failure explicitly.

### Phase 5 — Optional: ReportGenerator HTML/CSV reports (post-summary)

Phase 5 is **strictly optional** and runs **only after** Phase 4 has been delivered. Skip Phase 5 entirely when:

- The user supplied existing Cobertura XML and only asked for analysis (the default for the existing-data path).
- The user is diagnosing a coverage plateau or asking "what's blocking me?" — they want the answer, not a static-site report.
- ReportGenerator is not already installed and you have no clear signal the user wants HTML reports.

Run Phase 5 only when the user explicitly asks for HTML/CSV reports, or when the project flow requires them (e.g., a CI artifact upload step).

#### Step 6: Verify or install ReportGenerator (only if running Phase 5)

```powershell
$rgAvailable = $false
$rgCommand = Get-Command reportgenerator -ErrorAction SilentlyContinue
if ($rgCommand) {
    $rgAvailable = $true
    Write-Host "RG_INSTALLED:already-present"
} else {
    $rgToolPath = Join-Path "<COVERAGE_DIR>" ".tools"
    dotnet tool install dotnet-reportgenerator-globaltool --tool-path $rgToolPath
    if ($LASTEXITCODE -eq 0) {
        $env:PATH = "$rgToolPath$([System.IO.Path]::PathSeparator)$env:PATH"
        $rgCommand = Get-Command reportgenerator -ErrorAction SilentlyContinue
        if ($rgCommand) {
            $rgAvailable = $true
            Write-Host "RG_INSTALLED:true (tool-path: $rgToolPath)"
        } else {
            Write-Host "RG_INSTALLED:false"
            Write-Host "RG_INSTALL_ERROR:reportgenerator-not-available"
        }
    } else {
        Write-Host "RG_INSTALLED:false"
        Write-Host "RG_INSTALL_ERROR:reportgenerator-not-available"
    }
}
Write-Host "RG_AVAILABLE:$rgAvailable"
```

If installation fails (no internet), keep `RG_AVAILABLE:false`, leave the existing user-facing summary as the final output, and note that HTML reports were skipped.

#### Step 7: Generate HTML/CSV reports

```powershell
$reportsDir = Join-Path "<COVERAGE_DIR>" "reports"
if ($rgAvailable) {
    reportgenerator `
        -reports:"<semicolon-separated COBERTURA paths>" `
        -targetdir:$reportsDir `
        -reporttypes:"Html;TextSummary;MarkdownSummaryGithub;CsvSummary" `
        -title:"Coverage Report" `
        -tag:"coverage-analysis-skill"

    Get-Content (Join-Path $reportsDir "Summary.txt") -ErrorAction SilentlyContinue
} else {
    Write-Host "REPORTGENERATOR_SKIPPED:true"
}
```

After Phase 5 completes successfully, you may follow up with a short message pointing the user to the generated HTML report (one paragraph, no need to repeat the summary).

## Validation

- Verify that at least one `coverage.cobertura.xml` file was generated after `dotnet test` (or already exists when the user supplied one)
- Confirm the assistant response contained the CRAP/risk-hotspot table — saving the markdown file is secondary
- Confirm `TestResults/coverage-analysis/coverage-analysis.md` was written and contains data
- Spot-check one method's CRAP score: `comp² × (1 − cov)³ + comp` — a method with 100% coverage should have CRAP = complexity
- If Phase 5 ran, verify `TestResults/coverage-analysis/reports/index.html` exists; otherwise the report file should mark HTML/Text/CSV rows as `Not generated`

## Common Pitfalls

- **No Cobertura XML generated** — the test project may lack a coverage provider. The skill auto-adds one, but if `dotnet add package` fails (offline/proxy), coverage collection silently produces nothing. Check for `.coverage` binary files as a fallback indicator.
- **Test failures (exit code 1)** — coverage is still collected from passing tests. Do not abort; proceed with partial data and note the failures in the summary.
- **Premature end before user-facing summary** — never start Phase 5 (ReportGenerator install/run) before the Phase 4 assistant response is delivered. The heavy `dotnet tool install` can crash the session or exhaust budget, leaving the user with no analysis even though the CRAP scores were already computed.
- **ReportGenerator install failure** — if `dotnet tool install` fails (no internet) during Phase 5, leave the existing Phase 4 summary as the final output and note that HTML reports were skipped. Do not retry or block on the install.
- **Method name mismatches in Cobertura** — async methods, lambdas, and local functions may have compiler-generated names. The scripts use the Cobertura method name/signature directly; verify against source if results look unexpected.
- **Mixed coverage providers** — when a solution contains both Coverlet and Microsoft CodeCoverage projects, the skill runs per-project to avoid dual-provider conflicts. This is slower but correct.
