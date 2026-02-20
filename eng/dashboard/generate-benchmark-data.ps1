<#
.SYNOPSIS
    Converts skill-validator results into benchmark dashboard data.

.DESCRIPTION
    Reads skill-validator verdict.json and results.json files and produces a
    per-component JSON file (<ComponentName>.json) compatible with the benchmark
    dashboard. If an existing JSON file is provided, the new data point is
    appended to the existing history.

.PARAMETER ResultsDir
    Path to the skill-validator run results directory (e.g. .skill-validator-results/run-<timestamp>).

.PARAMETER ComponentName
    Name of the component these results belong to. Used as the output filename.

.PARAMETER OutputDir
    Path to write the output files. Defaults to ResultsDir.

.PARAMETER ExistingDataFile
    Optional path to an existing <ComponentName>.json file from gh-pages to append to.

.PARAMETER CommitJson
    Optional JSON string with commit info (id, message, author, timestamp, url).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultsDir,

    [Parameter(Mandatory)]
    [string]$ComponentName,

    [Parameter()]
    [string]$OutputDir,

    [Parameter()]
    [string]$ExistingDataFile,

    [Parameter()]
    [string]$CommitJson
)

$ErrorActionPreference = "Stop"

if (-not $OutputDir) {
    $OutputDir = $ResultsDir
}

# Read skill-validator results
$resultsFile = Join-Path $ResultsDir "results.json"
if (-not (Test-Path $resultsFile)) {
    Write-Warning "No results.json found in $ResultsDir"
    exit 0
}

$results = Get-Content $resultsFile -Raw | ConvertFrom-Json
$model = $results.model

# Find verdict files for skills in this component
$skillDirs = Get-ChildItem -Path $ResultsDir -Directory -ErrorAction SilentlyContinue
if (-not $skillDirs) {
    Write-Warning "No skill results found in $ResultsDir"
    exit 0
}

# Build bench arrays for this run
$qualityBenches = [System.Collections.Generic.List[object]]::new()
$efficiencyBenches = [System.Collections.Generic.List[object]]::new()

foreach ($skillDir in $skillDirs) {
    $verdictFile = Join-Path $skillDir.FullName "verdict.json"
    if (-not (Test-Path $verdictFile)) { continue }

    $verdict = Get-Content $verdictFile -Raw | ConvertFrom-Json
    $skillName = $verdict.skillName

    foreach ($scenario in $verdict.scenarios) {
        $testName = "$skillName/$($scenario.scenarioName)"

        # Quality scores (from judge results, scale 0-5 mapped to 0-10 for dashboard)
        if ($scenario.withSkill.judgeResult.overallScore) {
            $qualityBenches.Add(@{
                name  = "$testName - Skilled Quality"
                unit  = "Score (0-10)"
                value = [float]$scenario.withSkill.judgeResult.overallScore * 2
            })
        }
        if ($scenario.baseline.judgeResult.overallScore) {
            $qualityBenches.Add(@{
                name  = "$testName - Vanilla Quality"
                unit  = "Score (0-10)"
                value = [float]$scenario.baseline.judgeResult.overallScore * 2
            })
        }

        # Efficiency metrics (from with-skill run)
        if ($scenario.withSkill.metrics.wallTimeMs) {
            $efficiencyBenches.Add(@{
                name  = "$testName - Skilled Time"
                unit  = "seconds"
                value = [math]::Round([float]$scenario.withSkill.metrics.wallTimeMs / 1000, 1)
            })
        }
        if ($scenario.withSkill.metrics.tokenEstimate) {
            $efficiencyBenches.Add(@{
                name  = "$testName - Skilled Tokens In"
                unit  = "tokens"
                value = [float]$scenario.withSkill.metrics.tokenEstimate
            })
        }
    }
}

# Build commit info
$commit = @{}
if ($CommitJson) {
    $commit = $CommitJson | ConvertFrom-Json -AsHashtable
} else {
    $commit = @{ id = "local"; message = "Local run"; timestamp = (Get-Date -Format "o") }
}

$now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()

$qualityEntry = @{
    commit = $commit
    date   = $now
    tool   = "customBiggerIsBetter"
    model  = $model
    benches = $qualityBenches.ToArray()
}

$efficiencyEntry = @{
    commit = $commit
    date   = $now
    tool   = "customSmallerIsBetter"
    model  = $model
    benches = $efficiencyBenches.ToArray()
}

$qualityKey = "Quality"
$efficiencyKey = "Efficiency"

# Load existing data or create new structure
$benchmarkData = @{
    lastUpdate = $now
    repoUrl    = ""
    entries    = @{
        $qualityKey    = @()
        $efficiencyKey = @()
    }
}

if ($ExistingDataFile -and (Test-Path $ExistingDataFile)) {
    $existingContent = Get-Content $ExistingDataFile -Raw
    try {
        $benchmarkData = $existingContent | ConvertFrom-Json -AsHashtable
        $benchmarkData['lastUpdate'] = $now
    } catch {
        Write-Warning "Failed to parse existing data file, starting fresh: $_"
    }
}

# Append new entries
if (-not $benchmarkData['entries']) {
    $benchmarkData['entries'] = @{}
}
if (-not $benchmarkData['entries'][$qualityKey]) {
    $benchmarkData['entries'][$qualityKey] = @()
}
if (-not $benchmarkData['entries'][$efficiencyKey]) {
    $benchmarkData['entries'][$efficiencyKey] = @()
}

$benchmarkData['entries'][$qualityKey] += @($qualityEntry)
$benchmarkData['entries'][$efficiencyKey] += @($efficiencyEntry)

# Write <ComponentName>.json
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$dataJson = $benchmarkData | ConvertTo-Json -Depth 10
$dataJsonFile = Join-Path $OutputDir "$ComponentName.json"
$dataJson | Out-File -FilePath $dataJsonFile -Encoding utf8

Write-Host "[OK] Benchmark $ComponentName.json generated: $dataJsonFile"
Write-Host "   Quality entries: $($qualityBenches.Count)"
Write-Host "   Efficiency entries: $($efficiencyBenches.Count)"
Write-Host "   Total data points: $($benchmarkData['entries'][$qualityKey].Count)"
