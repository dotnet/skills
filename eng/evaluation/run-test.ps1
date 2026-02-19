<#
.SYNOPSIS
    Runs a single evaluation test against Copilot CLI.

.DESCRIPTION
    Copies the test's project files to a clean temp directory,
    executes Copilot CLI in programmatic mode there, captures output and stats,
    saves results to the results directory, and cleans up the temp copy.

.PARAMETER TestName
    Name of the test folder under the tests base directory.
    Each test folder must contain project files and optionally
    an 'expected-output.md' for evaluation and 'eval-test-prompt.txt' for custom prompts.

.PARAMETER RunType
    Either "vanilla" (no components) or "skilled" (with the skills component).

.PARAMETER ResultsDir
    Path to the results directory for this run.

.PARAMETER TimeoutSeconds
    Maximum time to wait for Copilot CLI to complete (default: 300).

.PARAMETER TestsBaseDir
    Path to the tests directory. Can be relative (resolved against RepoRoot)
    or absolute.

.PARAMETER RepoRoot
    Root directory of the repository. Defaults to two levels up from this script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TestName,

    [Parameter(Mandatory)]
    [ValidateSet("vanilla", "skilled")]
    [string]$RunType,

    [Parameter(Mandatory)]
    [string]$ResultsDir,

    [int]$TimeoutSeconds = 300,

    [Parameter(Mandatory)]
    [int]$MaxRetries,

    [string]$TestsBaseDir,

    [Parameter(Mandatory)]
    [string]$RunId,

    [Parameter(Mandatory)]
    [string]$Model,

    [string]$RepoRoot
)
$ErrorActionPreference = "Stop"

# Resolve repo root
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\")).Path
}

# Import helper functions
. (Join-Path $PSScriptRoot "invoke-copilot.ps1")
. (Join-Path $PSScriptRoot "parse-copilot-stats.ps1")

#region Helper Functions

function Get-SkillActivation {
    param(
        [string]$ConfigDir
    )

    $sessionStateDir = Join-Path $ConfigDir "session-state"
    if (-not (Test-Path $sessionStateDir)) {
        Write-Warning "[ACTIVATION] No session-state directory found at $sessionStateDir"
        return @{
            SessionId = $null
            Skills    = @()
            Agents    = @()
            Activated = $false
        }
    }

    $sessionDir = Get-ChildItem $sessionStateDir -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $sessionDir) {
        Write-Warning "[ACTIVATION] No session directories found in $sessionStateDir"
        return @{
            SessionId = $null
            Skills    = @()
            Agents    = @()
            Activated = $false
        }
    }

    $eventsFile = Join-Path $sessionDir.FullName "events.jsonl"
    if (-not (Test-Path $eventsFile)) {
        Write-Warning "[ACTIVATION] No events.jsonl found at $eventsFile"
        return @{
            SessionId = $sessionDir.Name
            Skills    = @()
            Agents    = @()
            Activated = $false
        }
    }

    $lines = Get-Content $eventsFile -ErrorAction SilentlyContinue
    $skills = @()
    $agents = @()

    foreach ($line in $lines) {
        $e = $line | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($null -eq $e) { continue }
        # Check for skill tool calls
        if ($e.type -eq "tool.execution_start" -and $e.data.toolName -eq "skill") {
            $skillName = $e.data.arguments.skill
            if ($skillName -and $skills -notcontains $skillName) {
                $skills += $skillName
            }
        }
        # Check for subagent delegation
        if ($e.type -eq "subagent.started") {
            $agentName = $e.data.agentName
            if ($agentName -and $agents -notcontains $agentName) {
                $agents += $agentName
            }
        }
    }

    return @{
        SessionId = $sessionDir.Name
        Skills    = $skills
        Agents    = $agents
        Activated = ($skills.Count -gt 0 -or $agents.Count -gt 0)
    }
}

function Copy-TestToTemp {
    param(
        [string]$TestSourceDir,
        [string]$TestName,
        [string]$RunType
    )

    $tempBase = Join-Path ([System.IO.Path]::GetTempPath()) "copilot-eval"
    $tempDir = Join-Path $tempBase "${TestName}-${RunType}-$(Get-Random)"

    Write-Host "[COPY] Copying test to temp directory: $tempDir"
    New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
    Copy-Item -Path "$TestSourceDir\*" -Destination $tempDir -Recurse -Force

    # Remove evaluation and documentation files from temp copy
    $excludeFiles = @("expected-output.md", "eval-test-prompt.txt", "README.md", ".gitignore")
    foreach ($file in $excludeFiles) {
        $filePath = Join-Path $tempDir $file
        if (Test-Path $filePath) {
            Remove-Item $filePath -Force
            Write-Host "[CLEAN] Excluded $file from working directory"
        }
    }

    Write-Host "[OK] Test copied to clean working directory"

    return $tempDir
}

#endregion

#region Main Logic

Write-Host ""
Write-Host ("=" * 60)
Write-Host "[TEST] Running: $TestName ($RunType)"
Write-Host ("=" * 60)

if (-not $TestsBaseDir) {
    throw "TestsBaseDir is required."
}
if (-not [System.IO.Path]::IsPathRooted($TestsBaseDir)) {
    $TestsBaseDir = Join-Path $RepoRoot $TestsBaseDir
}
$testBaseDir = Join-Path $TestsBaseDir $TestName
$testSourceDir = $testBaseDir
$testResultsDir = Join-Path $ResultsDir $TestName $RunId

if (-not (Test-Path $testSourceDir)) {
    throw "Test source directory not found: $testSourceDir"
}

# Create results directory
New-Item -ItemType Directory -Force -Path $testResultsDir | Out-Null

# Step 1: Copy test to a clean temp directory
$workingDir = Copy-TestToTemp -TestSourceDir $testSourceDir -TestName $TestName -RunType $RunType

# Step 2: Build the prompt
# Read eval-test-prompt.txt from the ORIGINAL test dir (before exclusion)
$promptFile = Join-Path $testBaseDir "eval-test-prompt.txt"
if (Test-Path $promptFile) {
    $prompt = (Get-Content $promptFile -Raw).Trim()
    Write-Host "[PROMPT] Loaded from: $promptFile"
} else {
    $prompt = "Analyze the build issues in this project and provide required fixes and their explanations. The fixes should not alter logic of the code (e.g. by suggesting to delete code files)."
    Write-Host "[PROMPT] Using default prompt (no eval-test-prompt.txt found)"
}

# Step 3: Run Copilot CLI
$outputFile = Join-Path $testResultsDir "${RunType}-output.txt"

# Create a per-run config directory for session isolation (must be absolute path)
$sessionConfigDir = Join-Path (Resolve-Path $testResultsDir).Path "${RunType}-config"
New-Item -ItemType Directory -Force -Path $sessionConfigDir | Out-Null
Write-Host "[SESSION] Config directory: $sessionConfigDir"

$output = $null
for ($attempt = 1; $attempt -le $MaxRetries; $attempt++) {
    if ($attempt -gt 1) {
        Write-Host "[RETRY] Attempt $attempt of $MaxRetries..."
        # Clean up previous session config for fresh retry
        Remove-Item -Recurse -Force $sessionConfigDir -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $sessionConfigDir | Out-Null
    }

    $output = Invoke-CopilotCli `
        -Prompt $prompt `
        -WorkingDir $workingDir `
        -OutputFile $outputFile `
        -TimeoutSeconds $TimeoutSeconds `
        -ConfigDir $sessionConfigDir `
        -IncludeStderr `
        -Model $Model

    if ($null -ne $output) {
        break
    }
    $delay = 60 * $attempt
    Write-Warning "[RETRY] Attempt $attempt failed (timeout or error), retrying in ${delay}s..."
    Start-Sleep -Seconds $delay
}
if ($null -eq $output) {
    throw "All $MaxRetries attempts failed for $TestName ($RunType)"
}

# Step 4: Parse stats
Write-Host ""
Write-Host "[STATS] Parsing stats..."
$stats = Parse-CopilotStats -Output $output

# Add metadata
$stats.RunType = $RunType
$stats.TestName = $TestName
$stats.Timestamp = (Get-Date -Format "o")

$statsFile = Join-Path $testResultsDir "${RunType}-stats.json"
$stats | ConvertTo-Json -Depth 5 | Out-File -FilePath $statsFile -Encoding utf8

Write-Host "   Premium Requests: $($stats.PremiumRequests)"
Write-Host "   API Time: $($stats.ApiTimeSeconds)s"
Write-Host "   Total Time: $($stats.TotalTimeSeconds)s"
Write-Host "   Model: $($stats.Model)"
Write-Host "   Tokens In: $($stats.TokensIn)"
Write-Host "   Tokens Out: $($stats.TokensOut)"

# Step 4b: Extract skill activation from session logs
Write-Host ""
Write-Host "[ACTIVATION] Checking skill activation from session logs..."
$activation = Get-SkillActivation -ConfigDir $sessionConfigDir

$activationFile = Join-Path $testResultsDir "${RunType}-activations.json"
$activation | ConvertTo-Json -Depth 5 | Out-File -FilePath $activationFile -Encoding utf8

if ($activation.Activated) {
    Write-Host "   Skills activated: $($activation.Skills -join ', ')"
    if ($activation.Agents.Count -gt 0) {
        Write-Host "   Agents delegated: $($activation.Agents -join ', ')"
    }
} else {
    if ($RunType -eq "skilled") {
        Write-Host "   WARNING: No skills or agents were activated in skilled run"
    } else {
        Write-Host "   (vanilla run - no skills expected)"
    }
}

# Save session ID for reproducibility
if ($activation.SessionId) {
    $sessionInfo = @{
        SessionId = $activation.SessionId
        ConfigDir = $sessionConfigDir
        RunType   = $RunType
        Test      = $TestName
        Timestamp = (Get-Date -Format "o")
    }
    $sessionFile = Join-Path $testResultsDir "${RunType}-session.json"
    $sessionInfo | ConvertTo-Json -Depth 5 | Out-File -FilePath $sessionFile -Encoding utf8
    Write-Host "   Session ID: $($activation.SessionId)"
}

# Step 5: Clean up temp directory
Write-Host ""
Write-Host "[CLEAN] Removing temp working directory: $workingDir"
Remove-Item -Path $workingDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "[OK] Test $TestName ($RunType) completed"
Write-Host "   Output: $outputFile"
Write-Host "   Stats: $statsFile"

#endregion
