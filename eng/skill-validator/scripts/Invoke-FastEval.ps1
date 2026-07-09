<#
.SYNOPSIS
    Fast, orchestrated skill-validator sweeps: split execute/judge, fast first-pass
    judging, and shared-baseline reuse — built entirely on existing `evaluate`
    subcommands (no core-engine changes).

.DESCRIPTION
    Wraps the skill-validator `evaluate`, `evaluate rejudge`, and `evaluate consolidate`
    subcommands into two orchestration profiles that remove the biggest serial costs
    from a large evaluation sweep:

      -Mode Split (default)   Lever #4. Runs the agent arms with `--no-judge
                              --keep-sessions` (one results dir per skill = one shard),
                              then `rejudge` each shard, then `consolidate`. This takes
                              the serial judge tail off the critical path and lets the
                              execute phase run flat-out in parallel; judging fans out
                              across shards afterward.

      -Mode BaselineReuse     Lever #2. Computes the skill-independent baseline arm ONCE
                              with `--baseline-out`, then evaluates every remaining skill
                              with `--baseline-from` so the baseline arm is never
                              recomputed (removes up to ~1/3 of agent runs). This path
                              judges inline (baseline reuse and `--no-judge` are mutually
                              exclusive in the validator).

    -Fast (lever #5) layers a cheaper first-pass judging profile on either mode:
    a faster judge model, a shorter judge timeout, and (BaselineReuse only)
    `--no-overfitting-check`. Escalate borderline skills to a full run afterward
    (see eng/skill-validator/src/docs/FastEvaluation.md).

.EXAMPLE
    # Split execute/judge across three skills, fast first pass, consolidated summary.
    ./Invoke-FastEval.ps1 -Mode Split -Fast `
        -TestsDir ./tests/dotnet-msbuild `
        -Skills ./plugins/dotnet-msbuild/skills/binlog-generation, `
                ./plugins/dotnet-msbuild/skills/incremental-build `
        -Output ./summary.md

.EXAMPLE
    # Reuse one baseline across many skills (inline judged), full-fidelity judging.
    ./Invoke-FastEval.ps1 -Mode BaselineReuse `
        -TestsDir ./tests/dotnet-msbuild `
        -Skills (Get-ChildItem ./plugins/dotnet-msbuild/skills -Directory).FullName `
        -Output ./summary.md
#>
[CmdletBinding()]
param(
    # Skill directories to evaluate. In Split mode each path is its own shard.
    [Parameter(Mandatory = $true)]
    [string[]] $Skills,

    # Directory containing the test subdirectories (skill-validator --tests-dir).
    [Parameter(Mandatory = $true)]
    [string] $TestsDir,

    [ValidateSet('Split', 'BaselineReuse')]
    [string] $Mode = 'Split',

    # Apply the fast first-pass judging profile (lever #5).
    [switch] $Fast,

    # Root results directory; per-shard/per-skill timestamped dirs are created under it.
    [string] $ResultsRoot = '.skill-validator-results',

    # Consolidated markdown summary output path.
    [string] $Output = 'fast-eval-summary.md',

    [string] $Model = 'claude-opus-4.6',

    # Judge model for full-fidelity judging (defaults to -Model).
    [string] $JudgeModel = '',

    # Judge model used when -Fast is set. Pick a cheaper/faster model.
    [string] $FastJudgeModel = 'claude-haiku-4.5',

    [int] $Runs = 5,

    # Judge timeout (seconds) for the full profile.
    [int] $JudgeTimeout = 300,

    # Judge timeout (seconds) when -Fast is set.
    [int] $FastJudgeTimeout = 120,

    # Max concurrent shards (Split) / skills (BaselineReuse) to run at once.
    [int] $MaxParallel = 3,

    # Executable used to launch the validator. Default: 'dotnet' with the freshly built
    # release dll, else a 'skill-validator' binary on PATH. Override to point at any binary.
    [string] $ValidatorExe = '',

    # Extra leading args passed before the subcommand (e.g. the dll path when using dotnet).
    # Leave empty to auto-detect alongside -ValidatorExe.
    [string[]] $ValidatorPrefixArgs = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Resolve how to invoke the validator ------------------------------------------------
# Returns @{ Exe = <exe>; Prefix = @(<leading args>) } so callers can splat with the call
# operator (& $exe @prefix @args) and never worry about quoting paths with spaces.
function Resolve-Validator {
    param([string] $Exe, [string[]] $Prefix)
    if ($Exe) { return @{ Exe = $Exe; Prefix = $Prefix } }
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    $dll = Join-Path $repoRoot 'artifacts\bin\SkillValidator\release\skill-validator.dll'
    if (Test-Path $dll) { return @{ Exe = 'dotnet'; Prefix = @($dll) } }
    if (Get-Command 'skill-validator' -ErrorAction SilentlyContinue) { return @{ Exe = 'skill-validator'; Prefix = @() } }
    throw "Could not find skill-validator. Build it (see docs) or pass -ValidatorExe."
}

$validator = Resolve-Validator -Exe $ValidatorExe -Prefix $ValidatorPrefixArgs
if (-not $JudgeModel) { $JudgeModel = $Model }
$effJudgeModel   = if ($Fast) { $FastJudgeModel } else { $JudgeModel }
$effJudgeTimeout = if ($Fast) { $FastJudgeTimeout } else { $JudgeTimeout }

# Synchronous, in-process validator call (used for rejudge/consolidate).
function Invoke-Validator {
    param([string[]] $ValArgs, [string] $Label)
    Write-Host "== $Label ==" -ForegroundColor Cyan
    Write-Host "   $($validator.Exe) $($validator.Prefix + $ValArgs -join ' ')" -ForegroundColor DarkGray
    & $validator.Exe @($validator.Prefix) @ValArgs
    return $LASTEXITCODE
}

# The scriptblock every background execute/reuse job runs. Pure: takes exe + full args.
$jobBlock = {
    param($Exe, $AllArgs)
    & $Exe @AllArgs 2>&1
    "EXITCODE:$LASTEXITCODE"
}

function Start-ValidatorJob {
    param([string] $Name, [string[]] $ValArgs)
    $allArgs = @($validator.Prefix) + $ValArgs
    Start-Job -Name $Name -ScriptBlock $jobBlock -ArgumentList $validator.Exe, $allArgs
}

# Throttle helper: block until fewer than $MaxParallel jobs from $list are running.
function Wait-ForSlot {
    param([System.Collections.Generic.List[object]] $List)
    while (@($List | Where-Object { $_.State -eq 'Running' }).Count -ge $MaxParallel) {
        Start-Sleep -Milliseconds 500
    }
}

function Complete-Jobs {
    param([System.Collections.Generic.List[object]] $List)
    $null = $List | Wait-Job
    foreach ($j in $List) {
        $out = Receive-Job $j
        if ($out) { Write-Host ($out | Out-String) }
        Remove-Job $j
    }
}

function Get-NewestTimestampDir {
    param([string] $Root)
    if (-not (Test-Path $Root)) { return $null }
    Get-ChildItem -Path $Root -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'sessions.db') } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Get-ShardName {
    param([string] $SkillPath)
    return (Split-Path -Leaf ($SkillPath.TrimEnd('\', '/')))
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$resultsJson = New-Object System.Collections.Generic.List[string]

Write-Host "skill-validator fast eval — mode=$Mode fast=$($Fast.IsPresent) runs=$Runs judge=$effJudgeModel" -ForegroundColor Green

if ($Mode -eq 'Split') {
    # -------- Phase 1: EXECUTE (agents only, no judging) — one shard per skill ----------
    $shards = foreach ($skill in $Skills) {
        $shardName = Get-ShardName $skill
        [pscustomobject]@{ Skill = $skill; Name = $shardName; Root = (Join-Path $ResultsRoot $shardName) }
    }

    Write-Host "`n[Phase 1/3] Executing $(@($shards).Count) shard(s) with --no-judge (max $MaxParallel parallel)..." -ForegroundColor Yellow
    $execJobs = [System.Collections.Generic.List[object]]::new()
    foreach ($shard in $shards) {
        Wait-ForSlot $execJobs
        $execArgs = @(
            'evaluate', $shard.Skill,
            '--tests-dir', $TestsDir,
            '--no-judge', '--keep-sessions',
            '--results-dir', $shard.Root,
            '--model', $Model,
            '--judge-model', $effJudgeModel,
            '--runs', "$Runs"
        )
        Write-Host "   -> execute shard '$($shard.Name)'" -ForegroundColor DarkGray
        $execJobs.Add((Start-ValidatorJob -Name "exec-$($shard.Name)" -ValArgs $execArgs))
    }
    Complete-Jobs $execJobs

    # -------- Phase 2: JUDGE (rejudge each shard's persisted sessions) ------------------
    Write-Host "`n[Phase 2/3] Rejudging shards (judge=$effJudgeModel, timeout=${effJudgeTimeout}s)..." -ForegroundColor Yellow
    foreach ($shard in $shards) {
        $tsDir = Get-NewestTimestampDir $shard.Root
        if (-not $tsDir) {
            Write-Warning "No sessions.db produced for shard '$($shard.Name)'; skipping rejudge."
            continue
        }
        $rjArgs = @(
            'evaluate', 'rejudge', $tsDir,
            '--judge-model', $effJudgeModel,
            '--judge-timeout', "$effJudgeTimeout"
        )
        $code = Invoke-Validator -ValArgs $rjArgs -Label "rejudge $($shard.Name)"
        $rj = Join-Path $tsDir 'results.json'
        if (Test-Path $rj) { $resultsJson.Add($rj) }
        else { Write-Warning "rejudge for '$($shard.Name)' produced no results.json (exit $code)." }
    }
}
else {
    # -------- BaselineReuse: compute baseline once, reuse it, judge inline -------------
    $baselineFile = Join-Path $ResultsRoot 'shared-baseline.json'
    $ofArg = if ($Fast) { @('--no-overfitting-check') } else { @() }

    Write-Host "`n[1/2] Evaluating first skill with --baseline-out (inline judged)..." -ForegroundColor Yellow
    $first = $Skills[0]
    $firstName = Get-ShardName $first
    $firstRoot = Join-Path $ResultsRoot $firstName
    $firstArgs = @(
        'evaluate', $first,
        '--tests-dir', $TestsDir,
        '--baseline-out', $baselineFile,
        '--results-dir', $firstRoot,
        '--keep-sessions',
        '--model', $Model,
        '--judge-model', $effJudgeModel,
        '--judge-timeout', "$effJudgeTimeout",
        '--runs', "$Runs"
    ) + $ofArg
    $null = Invoke-Validator -ValArgs $firstArgs -Label "baseline-out $firstName"
    $tsDir = Get-NewestTimestampDir $firstRoot
    if ($tsDir) { $rj = Join-Path $tsDir 'results.json'; if (Test-Path $rj) { $resultsJson.Add($rj) } }

    if (-not (Test-Path $baselineFile)) {
        throw "Baseline file was not produced at $baselineFile; cannot reuse. Aborting."
    }

    Write-Host "`n[2/2] Evaluating remaining skill(s) with --baseline-from (max $MaxParallel parallel)..." -ForegroundColor Yellow
    $rest = @($Skills | Select-Object -Skip 1)
    $jobs = [System.Collections.Generic.List[object]]::new()
    foreach ($skill in $rest) {
        Wait-ForSlot $jobs
        $name = Get-ShardName $skill
        $root = Join-Path $ResultsRoot $name
        $valArgs = @(
            'evaluate', $skill,
            '--tests-dir', $TestsDir,
            '--baseline-from', $baselineFile,
            '--results-dir', $root,
            '--keep-sessions',
            '--model', $Model,
            '--judge-model', $effJudgeModel,
            '--judge-timeout', "$effJudgeTimeout",
            '--runs', "$Runs"
        ) + $ofArg
        Write-Host "   -> reuse baseline for '$name'" -ForegroundColor DarkGray
        $jobs.Add((Start-ValidatorJob -Name "reuse-$name" -ValArgs $valArgs))
    }
    Complete-Jobs $jobs
    foreach ($skill in $rest) {
        $name = Get-ShardName $skill
        $tsDir = Get-NewestTimestampDir (Join-Path $ResultsRoot $name)
        if ($tsDir) { $rj = Join-Path $tsDir 'results.json'; if (Test-Path $rj) { $resultsJson.Add($rj) } }
    }
}

# --- Consolidate -----------------------------------------------------------------------
Write-Host "`n[Consolidate] Merging $($resultsJson.Count) results.json file(s) -> $Output" -ForegroundColor Yellow
if ($resultsJson.Count -eq 0) {
    Write-Warning "No results.json files were produced; nothing to consolidate."
    exit 1
}
$consArgs = @('evaluate', 'consolidate') + $resultsJson + @('--output', $Output)
$code = Invoke-Validator -ValArgs $consArgs -Label 'consolidate'

$sw.Stop()
Write-Host "`nDone in $([math]::Round($sw.Elapsed.TotalMinutes,1)) min. Summary: $Output" -ForegroundColor Green
exit $code
