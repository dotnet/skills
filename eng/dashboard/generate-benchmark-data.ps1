<#
.SYNOPSIS
    Converts evaluation results into benchmark dashboard data.

.DESCRIPTION
    Reads evaluation results from the results directory and produces a per-component
    JSON file (<ComponentName>.json) compatible with the benchmark dashboard.
    If an existing JSON file is provided, the new data point is appended to the
    existing history.

.PARAMETER ResultsDir
    Path to the results directory for this run.

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

    [Parameter(Mandatory)]
    [string]$RunId,

    [Parameter()]
    [string]$CommitJson
)

$ErrorActionPreference = "Stop"

if (-not $OutputDir) {
    $OutputDir = $ResultsDir
}

$testDirs = Get-ChildItem -Path $ResultsDir -Directory -ErrorAction SilentlyContinue
if (-not $testDirs) {
    Write-Warning "No test results found in $ResultsDir"
    exit 0
}

# Build bench arrays for this run
$qualityBenches = [System.Collections.Generic.List[object]]::new()
$efficiencyBenches = [System.Collections.Generic.List[object]]::new()

foreach ($testDir in $testDirs) {
    $testName = $testDir.Name
    $evalFile = Join-Path $testDir.FullName $RunId "evaluation.json"
    $skilledStatsFile = Join-Path $testDir.FullName $RunId "skilled-stats.json"

    if (Test-Path $evalFile) {
        $evalData = Get-Content $evalFile -Raw | ConvertFrom-Json
        $vanillaEval = $evalData.evaluations.vanilla
        $skilledEval = $evalData.evaluations.skilled

        if ($skilledEval -and $skilledEval.score) {
            $qualityBenches.Add(@{ name = "$testName - Skilled Quality"; unit = "Score (0-10)"; value = [float]$skilledEval.score })
        }

        if ($vanillaEval -and $vanillaEval.score) {
            $qualityBenches.Add(@{ name = "$testName - Vanilla Quality"; unit = "Score (0-10)"; value = [float]$vanillaEval.score })
        }
    }

    if (Test-Path $skilledStatsFile) {
        $skilledStats = Get-Content $skilledStatsFile -Raw | ConvertFrom-Json
        if ($skilledStats.TotalTimeSeconds) {
            $efficiencyBenches.Add(@{ name = "$testName - Skilled Time"; unit = "seconds"; value = [float]$skilledStats.TotalTimeSeconds })
        }
        if ($skilledStats.TokensIn) {
            $efficiencyBenches.Add(@{ name = "$testName - Skilled Tokens In"; unit = "tokens"; value = [float]$skilledStats.TokensIn })
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

# Detect model from skilled-stats.json files
$model = $null
foreach ($testDir in $testDirs) {
    $skilledStatsFile = Join-Path $testDir.FullName $RunId "skilled-stats.json"
    if (Test-Path $skilledStatsFile) {
        $skilledStats = Get-Content $skilledStatsFile -Raw | ConvertFrom-Json
        if ($skilledStats.Model) {
            $model = $skilledStats.Model
            break
        }
    }
}

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
$dataJson = $benchmarkData | ConvertTo-Json -Depth 10
$dataJsonFile = Join-Path $OutputDir "$ComponentName.json"
$dataJson | Out-File -FilePath $dataJsonFile -Encoding utf8

Write-Host "[OK] Benchmark $ComponentName.json generated: $dataJsonFile"
Write-Host "   Quality entries: $($qualityBenches.Count)"
Write-Host "   Efficiency entries: $($efficiencyBenches.Count)"
Write-Host "   Total data points: $($benchmarkData['entries'][$qualityKey].Count)"
