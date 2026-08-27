<#
.SYNOPSIS
    Converts evaluation results into benchmark dashboard data.

.DESCRIPTION
    Reads an adapter or legacy skill-validator results.json file (which contains all
    verdicts) and produces a per-plugin JSON file (<PluginName>.json) compatible
    with the benchmark dashboard. Version 2 dashboard data adds authoritative gate
    evidence, activation intent, reference-skill classification, judge excerpts,
    and source links to each quality entry. Existing history remains readable.
    If an existing JSON file is provided, the new data point is appended.

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

.PARAMETER SkillValueOnly
    When set, appends only the SkillValue entry and skips the Quality/Efficiency
    entries. Use this on the second and later judges of one executor model so a
    dual-judge run adds one SkillValue point per (model, judge) but still only one
    Quality/Efficiency point per executor model (those views key on model alone).

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

    [Parameter(ParameterSetName = 'Generate')]
    [switch]$SkillValueOnly,

    [Parameter(Mandatory, ParameterSetName = 'Purge')]
    [switch]$PurgeStaleFiles,

    [Parameter(Mandatory, ParameterSetName = 'Purge')]
    [string]$DataDir,

    [Parameter(Mandatory, ParameterSetName = 'Purge')]
    [Parameter(ParameterSetName = 'Generate')]
    [int]$RetentionDays
)

$ErrorActionPreference = "Stop"

function Test-ReferenceSkill {
    param(
        [string]$Plugin,
        [string]$Skill
    )

    $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $skillFile = Join-Path $repoRoot 'plugins' $Plugin 'skills' $Skill 'SKILL.md'
    if (-not (Test-Path $skillFile)) {
        return $false
    }

    $content = Get-Content $skillFile -Raw
    return $content -match '(?mi)^disable-model-invocation:\s*true\s*$'
}

function Get-ActivationStatus {
    param(
        [object]$Activation,
        [bool]$ExpectActivation,
        [bool]$IsReferenceSkill
    )

    if ($null -eq $Activation) {
        return "unknown"
    }

    $activated = $Activation.activated -eq $true
    if ($IsReferenceSkill) {
        return $(if ($activated) { "reference-activated" } else { "reference-dormant" })
    }
    if ($ExpectActivation) {
        return $(if ($activated) { "activated" } else { "missing-activation" })
    }
    return $(if ($activated) { "unexpected-activation" } else { "dormant-as-expected" })
}

function Get-PluginActivityStatus {
    param([object]$Activation)

    if ($null -eq $Activation) {
        return "unknown"
    }

    # The plugin arm reports aggregate skill activity, not the identity of the
    # target skill. Preserve that weaker meaning instead of claiming activation.
    return $(if ($Activation.activated -eq $true) { "plugin-activity-observed" } else { "plugin-no-activity-observed" })
}

# Mean of a running sum over a count, or $null when nothing was counted. Keeping
# a real $null (rather than 0) lets the dashboard show "n=0" honestly instead of
# a fake zero average.
function Get-MeanOrNull([double]$sum, [int]$count) {
    if ($count -gt 0) { return $sum / $count }
    return $null
}

function Update-SkillValueIndex([string]$Directory) {
    if (-not (Test-Path $Directory)) { return }

    $entries = [System.Collections.Generic.List[object]]::new()
    $reservedFiles = @(
        "components.json",
        "token-usage.json",
        "judge-comparison.json",
        "skill-value.json"
    )
    $pluginFiles = Get-ChildItem -Path $Directory -Filter "*.json" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin $reservedFiles } |
        Sort-Object Name

    foreach ($file in $pluginFiles) {
        try {
            $data = Get-Content $file.FullName -Raw | ConvertFrom-Json
            foreach ($entry in @($data.entries.SkillValue)) {
                if ($null -eq $entry) { continue }
                $entries.Add([ordered]@{
                    plugin     = $file.BaseName
                    commit     = $entry.commit
                    date       = $entry.date
                    model      = $entry.model
                    judgeModel = $entry.judgeModel
                    skills     = $entry.skills
                })
            }
        } catch {
            Write-Warning "Failed to add $($file.Name) to skill-value.json: $_"
        }
    }

    $index = [ordered]@{
        schemaVersion = 1
        lastUpdate    = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        entries       = $entries.ToArray()
    }
    $index | ConvertTo-Json -Depth 10 |
        Out-File -FilePath (Join-Path $Directory "skill-value.json") -Encoding utf8
    Write-Host "[OK] Skill Value index generated: $($entries.Count) entries"
}

# --- Purge mode: scan a data directory and remove stale files ---
if ($PurgeStaleFiles) {
    $cutoffMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - ([long]$RetentionDays * 24 * 60 * 60 * 1000)
    $dataFiles = Get-ChildItem -Path $DataDir -Filter "*.json" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin @("components.json", "skill-value.json") }
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
                # Snapshot the keys before replacing category arrays. Enumerating
                # the live hashtable key collection while assigning values can
                # throw "Collection was modified" in PowerShell.
                foreach ($category in @($data['entries'].Keys)) {
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
    # Rebuild after purge so the compact index cannot retain entries removed from
    # the source plugin histories.
    Update-SkillValueIndex $DataDir
    exit 0
}

# --- Generate mode: produce per-plugin benchmark data ---
if (-not $OutputDir) {
    $OutputDir = Split-Path $ResultsFile -Parent
}

# Read evaluation results
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

# Build commit info before verdict evidence so source links can target the evaluated revision.
$commit = @{}
if ($CommitJson) {
    $commit = $CommitJson | ConvertFrom-Json -AsHashtable
} else {
    $commit = @{ id = "local"; message = "Local run"; timestamp = (Get-Date -Format "o") }
}

# Build bench arrays for this run
$qualityBenches = [System.Collections.Generic.List[object]]::new()
$efficiencyBenches = [System.Collections.Generic.List[object]]::new()
$verdictEvidence = [System.Collections.Generic.List[object]]::new()

foreach ($verdict in $results.verdicts) {
    $skillName = $verdict.skillName
    $isReferenceSkill = Test-ReferenceSkill -Plugin $PluginName -Skill $skillName
    $activationScenarios = [System.Collections.Generic.List[object]]::new()
    $judgeRationales = [System.Collections.Generic.List[object]]::new()

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
        if ($sa -and -not $sa.activated -and $expectActivation -and -not $isReferenceSkill) {
            $notActivated = $true
        }

        $saPluginForEvidence = if ($scenario.PSObject.Properties['skillActivationPlugin']) { $scenario.skillActivationPlugin } else { $null }
        $activationScenarios.Add([ordered]@{
            scenarioName = $scenario.scenarioName
            expectation  = if ($isReferenceSkill) { "reference" } elseif ($expectActivation) { "active" } else { "dormant" }
            preferenceGateEligible = if ($scenario.PSObject.Properties['preferenceGateEligible']) {
                $scenario.preferenceGateEligible -ne $false
            } else {
                # Schema v3 counted every scenario in the preference gate, including dormancy.
                $true
            }
            isolated     = Get-ActivationStatus -Activation $sa -ExpectActivation $expectActivation -IsReferenceSkill $isReferenceSkill
            plugin       = if ($null -ne $saPluginForEvidence) {
                Get-PluginActivityStatus -Activation $saPluginForEvidence
            } else {
                $null
            }
        })

        # Prefer paired-judge evidence because it explains the W/T/L vote. Fall
        # back to legacy pairwise or independent-judge reasoning when necessary.
        $rationale = $null
        $rationaleDirection = $null
        if ($scenario.PSObject.Properties['trials'] -and $scenario.trials) {
            $selectedTrial = @($scenario.trials |
                Where-Object { $_.evidence -and -not $_.errored } |
                Select-Object -First 1)
            if ($selectedTrial.Count -gt 0) {
                $rationale = "$($selectedTrial[0].evidence)"
                $rationaleDirection = switch ($selectedTrial[0].winner) {
                    "treatment" { "better" }
                    "baseline" { "worse" }
                    "tie" { "none" }
                    default { $null }
                }
            }
        }
        if (-not $rationale -and $scenario.pairwiseResult.overallReasoning) {
            $rationale = "$($scenario.pairwiseResult.overallReasoning)"
            $rationaleDirection = switch ($scenario.pairwiseResult.overallWinner) {
                "skill" { "better" }
                "baseline" { "worse" }
                "tie" { "none" }
                default { $null }
            }
        }
        if (-not $rationale -and $scenario.skilledIsolated.judgeResult.overallReasoning) {
            $rationale = "$($scenario.skilledIsolated.judgeResult.overallReasoning)"
        }
        if ($rationale) {
            $rationale = $rationale.Trim()
            if ($rationale.Length -gt 1200) {
                $rationale = $rationale.Substring(0, 1199) + "…"
            }
            $judgeRationales.Add([ordered]@{
                scenarioName = $scenario.scenarioName
                direction    = $rationaleDirection
                rationale    = $rationale
            })
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
        if ($null -ne $plugin -and $null -ne $plugin.metrics.wallTimeMs) {
            $pluginTimeBench = @{
                name  = "$testName - Plugin Time"
                unit  = "seconds"
                value = [math]::Round([float]$plugin.metrics.wallTimeMs / 1000, 1)
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
            if ($scenarioTimedOut) {
                $pluginTokenBench.timedOut = $true
            }
            if ($overfittingSeverity) {
                $pluginTokenBench.overfitting = $overfittingSeverity
                $pluginTokenBench.overfittingScore = $overfittingScore
            }
            $efficiencyBenches.Add($pluginTokenBench)
        }

        # Efficiency metrics (from vanilla/baseline run, if exists)
        if ($null -ne $scenario.baseline -and $null -ne $scenario.baseline.metrics.wallTimeMs) {
            $efficiencyBenches.Add(@{
                name  = "$testName - Vanilla Time"
                unit  = "seconds"
                value = [math]::Round([float]$scenario.baseline.metrics.wallTimeMs / 1000, 1)
            })
        }
        if ($null -ne $scenario.baseline -and $null -ne $scenario.baseline.metrics.tokenEstimate) {
            $efficiencyBenches.Add(@{
                name  = "$testName - Vanilla Tokens In"
                unit  = "tokens"
                value = [float]$scenario.baseline.metrics.tokenEstimate
            })
        }
    }

    $gateEvidence = $null
    if ($verdict.PSObject.Properties['signTest'] -and $null -ne $verdict.signTest) {
        $voteCount = if ($null -ne $verdict.stimulusVoteCount) {
            [int]$verdict.stimulusVoteCount
        } elseif ($null -ne $verdict.trialCount) {
            [int]$verdict.trialCount
        } else {
            [int]$verdict.signTest.wins + [int]$verdict.signTest.ties + [int]$verdict.signTest.losses
        }
        $gateEvidence = [ordered]@{
            stimulusVoteCount = $voteCount
            wins              = [int]$verdict.signTest.wins
            ties              = [int]$verdict.signTest.ties
            losses            = [int]$verdict.signTest.losses
            discordant        = [int]$verdict.signTest.discordant
            direction         = $verdict.signTest.direction
            pValue            = $verdict.signTest.pValue
            alpha             = $verdict.signTest.alpha
            netWin            = $verdict.netWin
            minimumNetWin     = $verdict.practicalSignificance.minimum
            excludedStimulusCount = if ($verdict.PSObject.Properties['excludedScenarioEvidence']) {
                [int]$verdict.excludedScenarioEvidence.count
            } else {
                0
            }
        }
    }

    $links = [System.Collections.Generic.List[object]]::new()
    if ($commit.id -and "$($commit.id)" -match '^[0-9a-fA-F]{7,40}$') {
        $revision = "$($commit.id)"
        $sourceRelativePath = if ($skillName.StartsWith("agent.")) {
            $agentName = $skillName.Substring("agent.".Length)
            "plugins/$PluginName/agents/$agentName.agent.md"
        } else {
            "plugins/$PluginName/skills/$skillName/SKILL.md"
        }
        $links.Add([ordered]@{
            label = if ($skillName.StartsWith("agent.")) { "Agent source" } else { "Skill source" }
            url   = "https://github.com/dotnet/skills/blob/$revision/$sourceRelativePath"
        })
        $links.Add([ordered]@{
            label = "Eval source"
            url   = "https://github.com/dotnet/skills/blob/$revision/tests/$PluginName/$skillName/eval.yaml"
        })
    }
    if ($commit.url) {
        $links.Add([ordered]@{ label = "Commit"; url = "$($commit.url)" })
    }

    $verdictEvidence.Add([ordered]@{
        skillName          = $skillName
        skillKind          = if ($isReferenceSkill) { "reference" } else { "invocable" }
        state              = if ($verdict.PSObject.Properties['state']) { $verdict.state } else { $null }
        stateReason        = if ($verdict.PSObject.Properties['stateReason']) { $verdict.stateReason } else { $null }
        passed             = $verdict.passed -eq $true
        regressed          = $verdict.regressed -eq $true
        preferenceRegressed = $verdict.preferenceRegressed -eq $true
        reason             = $verdict.reason
        gateEvidence       = $gateEvidence
        activationContract = if ($verdict.PSObject.Properties['activationContract']) { $verdict.activationContract } else { $null }
        activationScenarios = $activationScenarios.ToArray()
        judgeRationales    = $judgeRationales.ToArray()
        links              = $links.ToArray()
    })
}

$now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()

$qualityEntry = @{
    commit          = $commit
    date            = $now
    tool            = "customBiggerIsBetter"
    model           = $model
    benches         = $qualityBenches.ToArray()
    verdictEvidence = $verdictEvidence.ToArray()
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
$skillValueKey = "SkillValue"

# --- Build Skill Value per-skill aggregates for this run ---
# One record per skill: baseline (without-skill) vs treatment (with-skill) arm
# metric means, the treatment activation count, and the aggregate pass matrix.
# Everything here comes straight from the verdict the adapter already emits, so
# no adapter change is needed. The dashboard's Skill Value view keys the trailing
# average by (skill, model, judgeModel) and gates on the sample sizes carried here.
$skillValueSkills = [System.Collections.Generic.List[object]]::new()
foreach ($verdict in $results.verdicts) {
    $skillName = $verdict.skillName

    $activationExpected = 0   # scenarios where the skill is expected to fire
    $activationFired    = 0   # of those, how many actually fired in the treatment arm
    $anyTimedOut        = $false

    $baseN = 0; $baseTime = 0.0; $baseTok = 0.0; $baseIn = 0.0; $baseOut = 0.0; $baseCR = 0.0; $baseCW = 0.0
    $treatN = 0; $treatTime = 0.0; $treatTok = 0.0; $treatIn = 0.0; $treatOut = 0.0; $treatCR = 0.0; $treatCW = 0.0
    # Transparency counts: how many scenarios carried metrics in each arm on its
    # own (before pairing). A large gap vs the paired count means one arm dropped
    # scenarios (e.g. treatment timeouts), which would bias an unpaired delta.
    $baseAvail = 0; $treatAvail = 0

    # Aggregate pass matrix (baselinePassed/treatmentPassed per counted trial).
    # NOTE: per adapt.mjs these per-arm pass booleans may include LLM-grader
    # results, so they are pass TELEMETRY, not an objective/deterministic gate.
    $passTotal = 0; $baselineFail = 0; $treatmentFail = 0

    foreach ($scenario in $verdict.scenarios) {
        # Activation is only meaningful where the scenario expects the skill to
        # fire; expect_activation:false scenarios are excluded from the ratio.
        $expectActivation = $true
        if ($scenario.PSObject.Properties['expectActivation'] -and $scenario.expectActivation -eq $false) {
            $expectActivation = $false
        }
        $sa = if ($scenario.PSObject.Properties['skillActivationIsolated']) { $scenario.skillActivationIsolated } else { $scenario.skillActivation }
        if ($expectActivation) {
            $activationExpected++
            if ($sa -and $sa.activated) { $activationFired++ }
        }

        if ($scenario.timedOut -eq $true) { $anyTimedOut = $true }

        # Probe both arms' metrics. Adapter already fills absent token fields with
        # 0, so a non-null metrics block with a numeric wallTimeMs is fully numeric.
        $skilled = if ($scenario.PSObject.Properties['skilledIsolated']) { $scenario.skilledIsolated } else { $scenario.withSkill }
        $tm = $skilled.metrics
        $bm = $scenario.baseline.metrics
        $treatHas = ($null -ne $tm -and $null -ne $tm.wallTimeMs)
        $baseHas  = ($null -ne $bm -and $null -ne $bm.wallTimeMs)
        if ($treatHas) { $treatAvail++ }
        if ($baseHas)  { $baseAvail++ }

        # Accumulate a scenario into the delta ONLY when BOTH arms have metrics, so
        # baseline and treatment means always describe the same scenario population
        # (a true paired delta). baseN == treatN == pairedN by construction.
        if ($treatHas -and $baseHas) {
            $treatN++
            $treatTime += [double]$tm.wallTimeMs
            $treatTok  += [double]$tm.tokenEstimate
            $treatIn   += [double]$tm.inputTokens
            $treatOut  += [double]$tm.outputTokens
            $treatCR   += [double]$tm.cacheReadTokens
            $treatCW   += [double]$tm.cacheWriteTokens

            $baseN++
            $baseTime += [double]$bm.wallTimeMs
            $baseTok  += [double]$bm.tokenEstimate
            $baseIn   += [double]$bm.inputTokens
            $baseOut  += [double]$bm.outputTokens
            $baseCR   += [double]$bm.cacheReadTokens
            $baseCW   += [double]$bm.cacheWriteTokens
        }

        # Aggregate pass matrix: count only trials that carry BOTH arms' boolean
        # pass result and did not error. A skill whose scenarios expose no such
        # booleans ends with passTotal=0 → the view shows the not-passed rate as N/A.
        foreach ($trial in @($scenario.trials)) {
            if ($null -eq $trial -or $trial.errored -eq $true) { continue }
            $bp = $trial.baselinePassed
            $tp = $trial.treatmentPassed
            if ($bp -is [bool] -and $tp -is [bool]) {
                $passTotal++
                if (-not $bp) { $baselineFail++ }
                if (-not $tp) { $treatmentFail++ }
            }
        }
    }

    $skillValueSkills.Add(@{
        skill              = $skillName
        activationExpected = $activationExpected
        activationFired    = $activationFired
        timedOut           = $anyTimedOut
        pairedN            = $baseN
        baseAvailable      = $baseAvail
        treatAvailable     = $treatAvail
        baseline = @{
            n          = $baseN
            timeMs     = (Get-MeanOrNull $baseTime $baseN)
            tokens     = (Get-MeanOrNull $baseTok  $baseN)
            tokensIn   = (Get-MeanOrNull $baseIn   $baseN)
            tokensOut  = (Get-MeanOrNull $baseOut  $baseN)
            cacheRead  = (Get-MeanOrNull $baseCR   $baseN)
            cacheWrite = (Get-MeanOrNull $baseCW   $baseN)
        }
        treatment = @{
            n          = $treatN
            timeMs     = (Get-MeanOrNull $treatTime $treatN)
            tokens     = (Get-MeanOrNull $treatTok  $treatN)
            tokensIn   = (Get-MeanOrNull $treatIn   $treatN)
            tokensOut  = (Get-MeanOrNull $treatOut  $treatN)
            cacheRead  = (Get-MeanOrNull $treatCR   $treatN)
            cacheWrite = (Get-MeanOrNull $treatCW   $treatN)
        }
        passTotal        = $passTotal
        baselineFail     = $baselineFail
        treatmentFail    = $treatmentFail
        hasPassData      = ($passTotal -gt 0)
    })
}

$skillValueEntry = @{
    commit     = $commit
    date       = $now
    model      = $model
    judgeModel = $results.judgeModel
    skills     = $skillValueSkills.ToArray()
}


# PR evaluations only collect token-usage data — skip benchmark history so PR
# runs don't contaminate the nightly benchmark results in <PluginName>.json.
# -SkipBenchmarkData also skips this section (used by publish-token-data).
if ($Source -ne 'pr' -and -not $SkipBenchmarkData) {
    # Load existing data or create new structure
    $benchmarkData = @{
        schemaVersion = 2
        lastUpdate = $now
        repoUrl    = ""
        entries    = @{
            $qualityKey    = @()
            $efficiencyKey = @()
            $skillValueKey = @()
        }
    }

    if ($ExistingDataFile -and (Test-Path $ExistingDataFile)) {
        $existingContent = Get-Content $ExistingDataFile -Raw
        try {
            $benchmarkData = $existingContent | ConvertFrom-Json -AsHashtable
            $benchmarkData['schemaVersion'] = 2
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
    if (-not $benchmarkData['entries'][$skillValueKey]) {
        $benchmarkData['entries'][$skillValueKey] = @()
    }

    # Quality/Efficiency key on executor model only, so emit them once per model.
    # On dual-judge runs the caller passes -SkillValueOnly for the second+ judge to
    # avoid duplicate same-model points that would inflate those trailing windows.
    if (-not $SkillValueOnly) {
        $benchmarkData['entries'][$qualityKey] += @($qualityEntry)
        $benchmarkData['entries'][$efficiencyKey] += @($efficiencyEntry)
    }
    $benchmarkData['entries'][$skillValueKey] += @($skillValueEntry)

    # Purge entries older than the retention window
    if ($RetentionDays -gt 0) {
        $cutoffMs = $now - ([long]$RetentionDays * 24 * 60 * 60 * 1000)

        foreach ($key in @($qualityKey, $efficiencyKey, $skillValueKey)) {
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
