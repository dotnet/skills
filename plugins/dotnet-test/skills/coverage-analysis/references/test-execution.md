# Coverage Analysis — test execution (Phase 2)

Read this file **only** when Phase 1 found no existing Cobertura XML (`EXISTING_COBERTURA_COUNT:0`) and fresh coverage must be produced. When a report already exists, skip straight to Phase 3.

## Step 3: Detect coverage provider and run `dotnet test` with coverage collection

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
