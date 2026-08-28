# Coverage Analysis — fresh test execution

Read this file **only** when discovery found no existing Cobertura XML
(`EXISTING_COBERTURA_COUNT:0`) and fresh coverage must be produced. When a report
already exists, return to the existing-data path in `SKILL.md`.

This automatic provider workflow is for `SDK_TEST_PROJECTS` only. Exclude every
`CLASSIC_TEST_PROJECT` before provider detection, package addition, restore, or
test execution. In a mixed solution, run each SDK-style test project separately
instead of running a solution entry point that would include classic projects.
Use a checked-in repository coverage script for the classic subset when one
exists; otherwise label the analysis partial and request its Cobertura report.
Never run `dotnet add package` against a `packages.config` project or introduce
`PackageReference` / an SDK-style conversion implicitly.

```powershell
$testProjects = @($sdkTestProjects)
if ($testProjects.Count -eq 0) {
    throw "No SDK-style test projects are eligible for automatic coverage collection."
}
```

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

Run one `dotnet test` per eligible entry point for the selected strategy:

- In an all-SDK solution, run a single command for the solution entry.
- In a mixed classic/SDK solution, run once per `SDK_TEST_PROJECT`; never run the solution entry.
- In `mixed-project` mode: run one command per test project, using that project's existing provider to avoid dual-provider conflicts.

```powershell
$sdkVersion = (dotnet --version 2>$null)
$major = if ($sdkVersion -match '^(\d+)\.') { [int]$Matches[1] } else { 9 }
$searchDir = (Get-Location).Path
$globalJson = $null
while ($searchDir -and -not $globalJson) {
    $candidate = Join-Path $searchDir "global.json"
    if (Test-Path -LiteralPath $candidate) {
        $globalJson = Get-Item -LiteralPath $candidate
        break
    }
    $parent = [System.IO.Directory]::GetParent($searchDir)
    $searchDir = if ($parent) { $parent.FullName } else { $null }
}
$configuredRunner = if ($globalJson) {
    (Get-Content $globalJson.FullName -Raw | ConvertFrom-Json).test.runner
} else {
    $null
}
$dotnetTestMode = if (
    $major -ge 10 -and
    $configuredRunner -eq "Microsoft.Testing.Platform"
) {
    "native-MTP"
} else {
    "VSTest"
}
$coverageEntries = if ($classicTestProjects.Count -gt 0) {
    @($sdkTestProjects | ForEach-Object {
        [pscustomobject]@{ Path = $_.FullName; Type = "Project" }
    })
} else {
    @([pscustomobject]@{ Path = "<ENTRY>"; Type = "<ENTRY_TYPE>" })
}
```

**Coverlet** (`coverlet.collector`):

```powershell
$rawDir = Join-Path "<COVERAGE_DIR>" "raw"
foreach ($entry in $coverageEntries) {
    dotnet test $entry.Path `
        --collect:"XPlat Code Coverage" `
        --results-directory $rawDir `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include="[*]*" `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[*.Tests]*,[*.Test]*,[*Tests]*,[*Test]*,[*.Specs]*,[*.Testing]*" `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.SkipAutoProps=true
}
```

**Microsoft CodeCoverage** (`Microsoft.Testing.Extensions.CodeCoverage`):

The command syntax depends on the `dotnet test` runner mode, not the SDK major
version alone. Native MTP mode on .NET 10+ accepts selectors and top-level
coverage options. VSTest mode — including .NET 10 VSTest mode bridging to an
MTP application — keeps the positional project/solution path and passes MTP
coverage arguments after `--`.

```powershell
$rawDir = Join-Path "<COVERAGE_DIR>" "raw"

foreach ($entry in $coverageEntries) {
    if ($dotnetTestMode -eq "native-MTP") {
        # Native MTP mode: selectors and coverage are top-level dotnet test options.
        $selector = if ($entry.Type -eq "Solution") { "--solution" } else { "--project" }
        dotnet test $selector $entry.Path `
            --results-directory $rawDir `
            --coverage `
            --coverage-output-format cobertura `
            --coverage-output $rawDir
    } else {
        # VSTest mode (including an MTP bridge): keep the positional path and
        # pass Microsoft.Testing.Platform arguments after the separator.
        dotnet test $entry.Path `
            --results-directory $rawDir `
            -- --coverage --coverage-output-format cobertura --coverage-output $rawDir
    }
}
```

**Mixed-project mode** (`Microsoft.Testing.Extensions.CodeCoverage` + `coverlet.collector` in the same solution):

```powershell
$rawDir = Join-Path "<COVERAGE_DIR>" "raw"
foreach ($tp in $testProjects) {
    $hasMsCodeCov = Select-String -Path $tp.FullName -Pattern 'Microsoft\.Testing\.Extensions\.CodeCoverage' -Quiet
    if ($hasMsCodeCov) {
        if ($dotnetTestMode -eq "native-MTP") {
            dotnet test --project $tp.FullName --results-directory $rawDir --coverage --coverage-output-format cobertura --coverage-output $rawDir
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
