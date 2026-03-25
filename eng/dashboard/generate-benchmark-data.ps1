<#
.SYNOPSIS
    Converts skill-validator results into benchmark dashboard data.

.DESCRIPTION
    Reads the skill-validator results.json file (which contains all verdicts) and
    produces a per-plugin JSON file (<PluginName>.json) compatible with the
    benchmark dashboard. If an existing JSON file is provided, the new data point
    is appended to the existing history.

    When -PurgeStaleFiles is used, scans a data directory for plugin JSON files and
    removes entries older than the retention window. Files left with no entries are
    deleted so they are excluded from the components.json manifest.

.PARAMETER ResultsFile
    Path to the skill-validator results.json file.

.PARAMETER PluginName
    Name of the plugin these results belong to. Used as the output filename.

.PARAMETER OutputDir
    Path to write the output files. Defaults to the directory containing ResultsFile.

.PARAMETER ExistingDataFile
    Optional path to an existing <PluginName>.json file from gh-pages to append to.

.PARAMETER CommitJson
    Optional JSON string with commit info (id, message, author, timestamp, url).

.PARAMETER PurgeStaleFiles
    When set, scans DataDir for plugin JSON files, purges entries older than the
    retention window, and deletes files that have no remaining entries.

.PARAMETER DataDir
    Directory containing plugin JSON files to purge. Required with -PurgeStaleFiles.

.PARAMETER SkipBenchmarkData
    When set, skips generation of benchmark <PluginName>.json entries. Use this when
    only token-usage data is needed, such as in the publish-token-data job.

.PARAMETER SkipTokenUsage
    When set, skips generation of token-usage.json entries. Use this when only
    benchmark data (<PluginName>.json) is needed, such as in the publish-eval-data job.

.PARAMETER RetentionDays
    Number of days of data to retain. Entries older than this are purged. Required for the
    Purge parameter set; optional for the Generate parameter set (no default value).
#>
[CmdletBinding(DefaultParameterSetName = 'Generate')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Generate')]
    [string]$ResultsFile,

    [Parameter(Mandatory, ParameterSetName = 'Generate')]
    [string]$PluginName,

    [Parameter(ParameterSetName = 'Generate')]
    [string]$OutputDir,

    [Parameter(ParameterSetName = 'Generate')]
    [string]$ExistingDataFile,

    [Parameter(ParameterSetName = 'Generate')]
    [string]$CommitJson,

    [Parameter(ParameterSetName = 'Generate')]
    [ValidateSet('scheduled', 'pr')]
    [string]$Source = 'scheduled',

    [Parameter(ParameterSetName = 'Generate')]
    [int]$PRNumber,

    [Parameter(ParameterSetName = 'Generate')]
    [string]$PRTitle,

    [Parameter(ParameterSetName = 'Generate')]
    [switch]$SkipBenchmarkData,

    [Parameter(ParameterSetName = 'Generate')]
    [switch]$SkipTokenUsage,

    [Parameter(Mandatory, ParameterSetName = 'Purge')]
    [switch]$PurgeStaleFiles,

    [Parameter(Mandatory, ParameterSetName = 'Purge')]
    [string]$DataDir,

    [Parameter(Mandatory, ParameterSetName = 'Purge')]
    [Parameter(ParameterSetName = 'Generate')]
    [int]$RetentionDays
)

$ErrorActionPreference = "Stop"

# --- Purge mode: scan a data directory and remove stale files ---
if ($PurgeStaleFiles) {
    $cutoffMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - ([long]$RetentionDays * 24 * 60 * 60 * 1000)
    $dataFiles = Get-ChildItem -Path $DataDir -Filter "*.json" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne "components.json" }
    foreach ($file in $dataFiles) {
        try {
            $data = Get-Content $file.FullName -Raw | ConvertFrom-Json -AsHashtable
            $hasRecentEntries = $false
            if (-not $data -or -not $data['entries']) { continue }

            # token-usage.json has a flat entries array; plugin files have categorized entries
            if ($file.Name -eq 'token-usage.json') {
                $data['entries'] = @($data['entries'] | Where-Object { $_.date -ge $cutoffMs })
                if ($data['entries'].Count -gt 0) { $hasRecentEntries = $true }
            } else {
                foreach ($category in $data['entries'].Keys) {
                    $data['entries'][$category] = @($data['entries'][$category] | Where-Object { $_.date -ge $cutoffMs })
                    if ($data['entries'][$category].Count -gt 0) { $hasRecentEntries = $true }
                }
            }
            if (-not $hasRecentEntries) {
                Remove-Item $file.FullName -Force
                Write-Host "[REMOVED] $($file.Name) — all entries older than $RetentionDays days"
            } else {
                $data | ConvertTo-Json -Depth 10 | Out-File -FilePath $file.FullName -Encoding utf8
            }
        } catch {
            Write-Warning "Failed to process $($file.Name) for purge: $_"
        }
    }
    exit 0
}

# --- Generate mode: produce per-plugin benchmark data ---
if (-not $OutputDir) {
    $OutputDir = Split-Path $ResultsFile -Parent
}

# Read skill-validator results
if (-not (Test-Path $ResultsFile)) {
    Write-Warning "Results file not found: $ResultsFile"
    exit 0
}

$results = Get-Content $ResultsFile -Raw | ConvertFrom-Json
$model = $results.model

if (-not $results.verdicts -or $results.verdicts.Count -eq 0) {
    Write-Warning "No verdicts found in $ResultsFile"
    exit 0
}

# Build bench arrays for this run
$qualityBenches = [System.Collections.Generic.List[object]]::new()
$efficiencyBenches = [System.Collections.Generic.List[object]]::new()

foreach ($verdict in $results.verdicts) {
    $skillName = $verdict.skillName

    foreach ($scenario in $verdict.scenarios) {
        $testName = "$skillName/$($scenario.scenarioName)"

        # Check per-scenario activation state (verdict-level skillNotActivated is a
        # roll-up across all scenarios and must NOT be used here — each datapoint
        # should reflect only its own scenario's activation result).
        $notActivated = $false
        # Determine whether activation is expected (defaults to true)
        $expectActivation = $true
        if ($scenario.PSObject.Properties['expectActivation'] -and $scenario.expectActivation -eq $false) {
            $expectActivation = $false
        }
        # Support both old (skillActivation) and new (skillActivationIsolated) JSON schemas
        $sa = if ($scenario.PSObject.Properties['skillActivationIsolated']) { $scenario.skillActivationIsolated } else { $scenario.skillActivation }
        if ($sa -and -not $sa.activated -and $expectActivation) {
            $notActivated = $true
        }

        # Check per-scenario timeout state
        $scenarioTimedOut = $false
        if ($scenario.timedOut -eq $true) {
            $scenarioTimedOut = $true
        }

        # Check overfitting state — use per-scenario assessment when available.
        # The verdict-level overfittingResult is a skill-wide aggregate; applying it
        # to every scenario would misrepresent scenarios that are fine.  We check
        # rubric and assertion assessments for this scenario and fall back to the
        # verdict-level result only when no per-scenario data exists.
        $overfittingSeverity = $null
        $overfittingScore = $null
        if ($verdict.overfittingResult -and $verdict.overfittingResult.severity -in @("Moderate", "High")) {
            $scenarioName = $scenario.scenarioName
            # Determine whether the overfittingResult carries per-scenario
            # breakdowns (rubricAssessments / assertionAssessments arrays).
            # When breakdowns exist we use them; when they don't (older schema)
            # we fall back to the verdict-level flag for every scenario.
            $hasBreakdowns = $verdict.overfittingResult.PSObject.Properties['rubricAssessments'] -or
                             $verdict.overfittingResult.PSObject.Properties['assertionAssessments'] -or
                             $verdict.overfittingResult.PSObject.Properties['promptAssessments']

            if ($hasBreakdowns) {
                $rubrics    = $verdict.overfittingResult.rubricAssessments    | Where-Object { $_.scenario -eq $scenarioName }
                $assertions = $verdict.overfittingResult.assertionAssessments | Where-Object { $_.scenario -eq $scenarioName }
                $prompts    = $verdict.overfittingResult.promptAssessments    | Where-Object { $_.scenario -eq $scenarioName }
                # Rubric classifications: outcome | technique | vocabulary  — flag non-outcome.
                # Assertion classifications: broad | narrow               — flag narrow.
                # Prompt issues: any prompt assessment for this scenario is a flag.
                $scenarioHasIssues = ($rubrics    | Where-Object { $_.classification -ne "outcome" }) -or
                                     ($assertions | Where-Object { $_.classification -eq "narrow" }) -or
                                     ($prompts   | Measure-Object).Count -gt 0
                if ($scenarioHasIssues) {
                    $overfittingSeverity = $verdict.overfittingResult.severity.ToLower()
                    $overfittingScore = $verdict.overfittingResult.score
                }
                # else: breakdowns exist but this scenario has no issues — leave unflagged
            } else {
                # No per-scenario breakdown available (older schema); fall back to verdict-level
                $overfittingSeverity = $verdict.overfittingResult.severity.ToLower()
                $overfittingScore = $verdict.overfittingResult.score
            }
        }

        # Support both old (withSkill) and new (skilledIsolated) JSON schemas
        $skilled = if ($scenario.PSObject.Properties['skilledIsolated']) { $scenario.skilledIsolated } else { $scenario.withSkill }

        # Plugin run (may not exist for older results or utility methods)
        $plugin = if ($scenario.PSObject.Properties['skilledPlugin']) { $scenario.skilledPlugin } else { $null }

        # Quality scores (from judge results, scale 0-5 mapped to 0-10 for dashboard)
        if ($null -ne $skilled.judgeResult.overallScore) {
            $benchEntry = @{
                name  = "$testName - Skilled Quality"
                unit  = "Score (0-10)"
                value = [float]$skilled.judgeResult.overallScore * 2
            }
            if ($notActivated) {
                $benchEntry.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $benchEntry.timedOut = $true
            }
            if ($overfittingSeverity) {
                $benchEntry.overfitting = $overfittingSeverity
                $benchEntry.overfittingScore = $overfittingScore
            }
            $qualityBenches.Add($benchEntry)
        }
        if ($null -ne $plugin -and $null -ne $plugin.judgeResult.overallScore) {
            $pluginBenchEntry = @{
                name  = "$testName - Plugin Quality"
                unit  = "Score (0-10)"
                value = [float]$plugin.judgeResult.overallScore * 2
            }
            # Plugin activation check
            $saPlugin = if ($scenario.PSObject.Properties['skillActivationPlugin']) { $scenario.skillActivationPlugin } else { $null }
            if ($saPlugin -and -not $saPlugin.activated -and $expectActivation) {
                $pluginBenchEntry.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $pluginBenchEntry.timedOut = $true
            }
            if ($overfittingSeverity) {
                $pluginBenchEntry.overfitting = $overfittingSeverity
                $pluginBenchEntry.overfittingScore = $overfittingScore
            }
            $qualityBenches.Add($pluginBenchEntry)
        }
        if ($null -ne $scenario.baseline.judgeResult.overallScore) {
            $qualityBenches.Add(@{
                name  = "$testName - Vanilla Quality"
                unit  = "Score (0-10)"
                value = [float]$scenario.baseline.judgeResult.overallScore * 2
            })
        }

        # Efficiency metrics (from with-skill isolated run)
        if ($null -ne $skilled.metrics.wallTimeMs) {
            $effBenchEntry = @{
                name  = "$testName - Skilled Time"
                unit  = "seconds"
                value = [math]::Round([float]$skilled.metrics.wallTimeMs / 1000, 1)
            }
            if ($notActivated) {
                $effBenchEntry.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $effBenchEntry.timedOut = $true
            }
            if ($overfittingSeverity) {
                $effBenchEntry.overfitting = $overfittingSeverity
                $effBenchEntry.overfittingScore = $overfittingScore
            }
            $efficiencyBenches.Add($effBenchEntry)
        }
        if ($null -ne $skilled.metrics.tokenEstimate) {
            $tokenBenchEntry = @{
                name  = "$testName - Skilled Tokens In"
                unit  = "tokens"
                value = [float]$skilled.metrics.tokenEstimate
            }
            if ($notActivated) {
                $tokenBenchEntry.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $tokenBenchEntry.timedOut = $true
            }
            if ($overfittingSeverity) {
                $tokenBenchEntry.overfitting = $overfittingSeverity
                $tokenBenchEntry.overfittingScore = $overfittingScore
            }
            $efficiencyBenches.Add($tokenBenchEntry)
        }

        # Efficiency metrics (from plugin run, if exists)
        # Compute plugin-specific notActivated signal for efficiency benches
        $pluginNotActivated = $false
        $saPlugin = if ($scenario.PSObject.Properties['skillActivationPlugin']) { $scenario.skillActivationPlugin } else { $null }
        if ($saPlugin -and -not $saPlugin.activated -and $expectActivation) {
            $pluginNotActivated = $true
        }
        if ($null -ne $plugin -and $null -ne $plugin.metrics.wallTimeMs) {
            $pluginTimeBench = @{
                name  = "$testName - Plugin Time"
                unit  = "seconds"
                value = [math]::Round([float]$plugin.metrics.wallTimeMs / 1000, 1)
            }
            if ($pluginNotActivated) {
                $pluginTimeBench.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $pluginTimeBench.timedOut = $true
            }
            if ($overfittingSeverity) {
                $pluginTimeBench.overfitting = $overfittingSeverity
                $pluginTimeBench.overfittingScore = $overfittingScore
            }
            $efficiencyBenches.Add($pluginTimeBench)
        }
        if ($null -ne $plugin -and $null -ne $plugin.metrics.tokenEstimate) {
            $pluginTokenBench = @{
                name  = "$testName - Plugin Tokens In"
                unit  = "tokens"
                value = [float]$plugin.metrics.tokenEstimate
            }
            if ($pluginNotActivated) {
                $pluginTokenBench.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $pluginTokenBench.timedOut = $true
            }
            if ($overfittingSeverity) {
                $pluginTokenBench.overfitting = $overfittingSeverity
                $pluginTokenBench.overfittingScore = $overfittingScore
            }
            $efficiencyBenches.Add($pluginTokenBench)
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

# PR evaluations only collect token-usage data — skip benchmark history so PR
# runs don't contaminate the nightly benchmark results in <PluginName>.json.
# -SkipBenchmarkData also skips this section (used by publish-token-data).
if ($Source -ne 'pr' -and -not $SkipBenchmarkData) {
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

    # Purge entries older than the retention window
    if ($RetentionDays -gt 0) {
        $cutoffMs = $now - ([long]$RetentionDays * 24 * 60 * 60 * 1000)

        foreach ($key in @($qualityKey, $efficiencyKey)) {
            $before = $benchmarkData['entries'][$key].Count
            $benchmarkData['entries'][$key] = @($benchmarkData['entries'][$key] | Where-Object {
                $_.date -ge $cutoffMs
            })
            $purged = $before - $benchmarkData['entries'][$key].Count
            if ($purged -gt 0) {
                Write-Host "   Purged $purged $key entries older than $RetentionDays days"
            }
        }
    }

    # Write <PluginName>.json
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    $dataJson = $benchmarkData | ConvertTo-Json -Depth 10
    $dataJsonFile = Join-Path $OutputDir "$PluginName.json"
    $dataJson | Out-File -FilePath $dataJsonFile -Encoding utf8

    Write-Host "[OK] Benchmark $PluginName.json generated: $dataJsonFile"
    Write-Host "   Quality entries: $($qualityBenches.Count)"
    Write-Host "   Efficiency entries: $($efficiencyBenches.Count)"
    Write-Host "   Total data points: $($benchmarkData['entries'][$qualityKey].Count)"
} else {
    # Ensure OutputDir exists even in PR mode (needed for token-usage.json)
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    Write-Host "[OK] Skipping benchmark $PluginName.json generation, collecting token usage only"
}

# --- Generate token-usage entries ---
if (-not $SkipTokenUsage) {
$tokenUsageEntries = [System.Collections.Generic.List[object]]::new()

foreach ($verdict in $results.verdicts) {
    $skillName = $verdict.skillName

    foreach ($scenario in $verdict.scenarios) {
        # Support both old (withSkill) and new (skilledIsolated) JSON schemas
        $skilled = if ($scenario.PSObject.Properties['skilledIsolated']) { $scenario.skilledIsolated } else { $scenario.withSkill }
        $plugin = if ($scenario.PSObject.Properties['skilledPlugin']) { $scenario.skilledPlugin } else { $null }

        # Collect token usage from isolated run
        $runs = @($skilled)
        if ($null -ne $plugin) { $runs += $plugin }

        foreach ($run in $runs) {
            $m = $run.metrics
            if ($null -eq $m) { continue }

            # Use granular fields when available; fall back to tokenEstimate
            # as a *total* (input+output) since that's how skill-validator computes it.
            $cacheRead = if ($null -ne $m.cacheReadTokens) { [int]$m.cacheReadTokens } else { 0 }
            $cacheWrite = if ($null -ne $m.cacheWriteTokens) { [int]$m.cacheWriteTokens } else { 0 }
            if ($null -ne $m.inputTokens -and $m.inputTokens -gt 0) {
                $tokensIn = [int]$m.inputTokens
                $tokensOut = if ($null -ne $m.outputTokens) { [int]$m.outputTokens } else { 0 }
                $totalTokens = $tokensIn + $tokensOut
            } else {
                # tokenEstimate = input + output; use it as total, derive in/out
                $tokensOut = if ($null -ne $m.outputTokens) { [int]$m.outputTokens } else { 0 }
                $totalTokens = [int]$m.tokenEstimate
                $tokensIn = [Math]::Max(0, $totalTokens - $tokensOut)
            }

            # Judge token fields (0 for older results without judge tracking)
            $judgeIn = if ($null -ne $m.judgeInputTokens) { [int]$m.judgeInputTokens } else { 0 }
            $judgeOut = if ($null -ne $m.judgeOutputTokens) { [int]$m.judgeOutputTokens } else { 0 }
            $judgeCacheRead = if ($null -ne $m.judgeCacheReadTokens) { [int]$m.judgeCacheReadTokens } else { 0 }
            $judgeCacheWrite = if ($null -ne $m.judgeCacheWriteTokens) { [int]$m.judgeCacheWriteTokens } else { 0 }
            $judgeTotal = $judgeIn + $judgeOut

            $entry = @{
                date              = $now
                source            = $Source
                plugin            = $PluginName
                skill             = $skillName
                tokensIn          = $tokensIn
                tokensOut         = $tokensOut
                cacheReadTokens   = $cacheRead
                cacheWriteTokens  = $cacheWrite
                totalTokens       = $totalTokens
                judgeTokensIn     = $judgeIn
                judgeTokensOut    = $judgeOut
                judgeCacheRead    = $judgeCacheRead
                judgeCacheWrite   = $judgeCacheWrite
                judgeTotalTokens  = $judgeTotal
                model             = $model
            }
            if ($Source -eq 'pr' -and $PRNumber) {
                $entry.prNumber = $PRNumber
                $entry.prTitle = $PRTitle
            }
            $tokenUsageEntries.Add($entry)
        }
    }
}

# Load existing token-usage.json or create new structure
$tokenUsageFile = Join-Path $OutputDir "token-usage.json"
$tokenUsageData = @{ entries = @() }

if (Test-Path $tokenUsageFile) {
    try {
        $tokenUsageData = Get-Content $tokenUsageFile -Raw | ConvertFrom-Json -AsHashtable
    } catch {
        Write-Warning "Failed to parse token-usage.json, starting fresh: $_"
    }
}

if (-not $tokenUsageData['entries']) {
    $tokenUsageData['entries'] = @()
}

$tokenUsageData['entries'] += @($tokenUsageEntries)

# Purge old token-usage entries
if ($RetentionDays -gt 0) {
    $cutoffMs = $now - ([long]$RetentionDays * 24 * 60 * 60 * 1000)
    $before = $tokenUsageData['entries'].Count
    $tokenUsageData['entries'] = @($tokenUsageData['entries'] | Where-Object { $_.date -ge $cutoffMs })
    $purged = $before - $tokenUsageData['entries'].Count
    if ($purged -gt 0) {
        Write-Host "   Purged $purged token-usage entries older than $RetentionDays days"
    }
}

$tokenUsageData | ConvertTo-Json -Depth 5 | Out-File -FilePath $tokenUsageFile -Encoding utf8
Write-Host "   Token usage entries added: $($tokenUsageEntries.Count) (total: $($tokenUsageData['entries'].Count))"
} # end if (-not $SkipTokenUsage)
