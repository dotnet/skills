---
name: coverage-analysis
description: >
  Automated, project-wide code coverage and CRAP (Change Risk Anti-Patterns)
  score analysis for .NET projects with existing unit tests. Auto-detects
  solution structure, runs coverage collection via `dotnet test` (supports both
  Microsoft.Testing.Extensions.CodeCoverage and Coverlet), generates reports via
  ReportGenerator, calculates CRAP scores per method, and surfaces risk
  hotspots — complex code with low test coverage that is dangerous to modify.
  Use when the user wants project-wide coverage analysis with risk
  prioritization, coverage gap identification, CRAP score computation
  across an entire solution, or to diagnose why coverage is stuck or
  plateaued and identify what methods are blocking improvement.
  DO NOT USE FOR: targeted single-method CRAP analysis (use crap-score skill),
  writing tests, running tests without coverage collection, applying test
  filters, producing TRX reports, or troubleshooting test execution (use
  run-tests for all of these).
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
- At least one test project referencing the production code (xUnit, NUnit, or MSTest)
- Internet access for `dotnet tool install` (ReportGenerator) on first run, or ReportGenerator already installed globally

The skill auto-detects coverage provider state per test project and selects the least-invasive execution strategy:

- unified Microsoft CodeCoverage when all projects use it,
- unified Coverlet when no project uses Microsoft CodeCoverage,
- per-project provider execution when the solution is truly mixed.

No pre-existing runsettings files or manually installed tools required.

## Workflow

If the user provides a path to existing Cobertura XML (or coverage data is already present in `TestResults/`), skip Steps 3–4 (test execution and provider detection) but **still run Steps 5–6** (ReportGenerator and CRAP score computation). The Risk Hotspots table and CRAP scores are mandatory in every output — they are the skill's core value-add over raw coverage numbers.

The workflow runs in four phases. Phases 2 and 3 each contain steps that can run in parallel to reduce total wall-clock time.

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
if ($testProjects.Count -eq 1) {
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
- If `TEST_PROJECTS:0` → stop: `No test projects found (expected projects with 'Test' or 'Spec' in the name). Ensure your solution has unit test projects before running coverage analysis.`

#### Step 2: Create the output directory

```powershell
$coverageDir = Join-Path $testOutputRoot "TestResults" "coverage-analysis"
if (Test-Path $coverageDir) { Remove-Item $coverageDir -Recurse -Force }
New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null
Write-Host "COVERAGE_DIR:$coverageDir"
```

#### Step 2b: Recommend ignoring `TestResults/`

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

### Phase 2 — Data collection (Steps 3 and 4 run in parallel)

Steps 3 and 4 are independent — start both simultaneously. `dotnet test` is the slowest step, and ReportGenerator setup doesn't need coverage files, so running them concurrently cuts wall time significantly.

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

#### Step 4: Verify or install ReportGenerator (parallel with Step 3)

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

If installation fails (no internet), keep `RG_AVAILABLE:false` and continue with raw Cobertura XML parsing + script-based analysis in Step 6. Skip HTML/Text/CSV report generation in Step 5 and note this in the output.

### Phase 3 — Analysis (Steps 5 and 6 run in parallel)

Once Phase 2 completes (coverage files available, ReportGenerator ready), start Steps 5 and 6 simultaneously — both read from the same Cobertura XML and produce independent outputs.

#### Step 5: Generate reports with ReportGenerator (parallel with Step 6)

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

#### Step 6: Calculate CRAP scores using the bundled script (parallel with Step 5)

Run `scripts/Compute-CrapScores.ps1` (co-located with this SKILL.md). It reads all Cobertura XML files, applies `CRAP(m) = comp² × (1 − cov)³ + comp` per method, and returns the top-N hotspots as JSON.

To locate the script: find the directory containing this skill's `SKILL.md` file (the skill loader provides this context), then resolve `scripts/Compute-CrapScores.ps1` relative to it. If the script path cannot be determined, calculate CRAP scores inline using the formula below.

```powershell
& "<skill-directory>/scripts/Compute-CrapScores.ps1" `
    -CoberturaPath @(<all COBERTURA file paths as array>) `
    -CrapThreshold <crap_threshold> `
    -TopN <top_n>
```

Script outputs: `TOTAL_METHODS:<n>`, `FLAGGED_METHODS:<n>`, `HOTSPOTS:<json>` (top-N sorted by CrapScore descending).

Also run `scripts/Extract-MethodCoverage.ps1` to get per-method coverage data for the Coverage Gaps table:

```powershell
& "<skill-directory>/scripts/Extract-MethodCoverage.ps1" `
    -CoberturaPath @(<all COBERTURA file paths as array>) `
    -CoverageThreshold <line_threshold> `
    -BranchThreshold <branch_threshold> `
    -Filter below-threshold
```

Script outputs: JSON array of methods below the coverage threshold, sorted by coverage ascending. Use this data to populate the Coverage Gaps by File table in the report.

### Phase 4 — Output (sequential)

#### Step 7: Build the output report

Compose the analysis and save it to `TestResults/coverage-analysis/coverage-analysis.md` under the test project directory. Print the full report to the console.

After saving the file, automatically open `TestResults/coverage-analysis/coverage-analysis.md` in the editor so the user can review it immediately.

- In editor-hosted environments (VS Code, Visual Studio, or other IDE hosts): open the file in the current host session/editor context after writing it.
- Do not launch a different app instance via hardcoded shell commands (for example `code`, `start`, or platform-specific open commands) unless the host has no native open-file mechanism.
- In CLI or non-editor environments: print the absolute report path and clearly state that the file was generated.

Do not ask for confirmation before opening the report file.

Use `references/output-format.md` verbatim for all fixed headings, table structures, symbols, and emoji in the generated report. Use `references/guidelines.md` for execution constraints, prioritization rules, and style.

## Validation

- Verify that at least one `coverage.cobertura.xml` file was generated after `dotnet test`
- Confirm `TestResults/coverage-analysis/coverage-analysis.md` was written and contains data
- Spot-check one method's CRAP score: `comp² × (1 − cov)³ + comp` — a method with 100% coverage should have CRAP = complexity
- If ReportGenerator ran, verify `TestResults/coverage-analysis/reports/index.html` exists

## Common Pitfalls

- **No Cobertura XML generated** — the test project may lack a coverage provider. The skill auto-adds one, but if `dotnet add package` fails (offline/proxy), coverage collection silently produces nothing. Check for `.coverage` binary files as a fallback indicator.
- **Test failures (exit code 1)** — coverage is still collected from passing tests. Do not abort; proceed with partial data and note the failures in the summary.
- **ReportGenerator install failure** — if `dotnet tool install` fails (no internet), skip HTML/CSV report generation and continue with raw Cobertura XML analysis + script-based CRAP scores. Note the skip in the report.
- **Method name mismatches in Cobertura** — async methods, lambdas, and local functions may have compiler-generated names. The scripts use the Cobertura method name/signature directly; verify against source if results look unexpected.
- **Mixed coverage providers** — when a solution contains both Coverlet and Microsoft CodeCoverage projects, the skill runs per-project to avoid dual-provider conflicts. This is slower but correct.
