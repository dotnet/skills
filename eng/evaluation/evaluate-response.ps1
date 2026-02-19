<#
.SYNOPSIS
    Evaluates a Copilot CLI response against expected output using Copilot as evaluator.

.DESCRIPTION
    Takes the actual Copilot output and compares it against the expected output file
    using a separate Copilot CLI invocation (vanilla, no components) as the evaluator.
    Files are written to a temp directory so Copilot can read them with file tools.

.PARAMETER TestName
    Name of the test to evaluate.

.PARAMETER ResultsDir
    Path to the results directory for this run.

.PARAMETER TestsBaseDir
    Path to the tests directory. Can be relative (resolved against RepoRoot)
    or absolute.

.PARAMETER TimeoutSeconds
    Maximum time to wait for evaluation Copilot CLI to complete.

.PARAMETER RepoRoot
    Root directory of the repository.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TestName,

    [Parameter(Mandatory)]
    [string]$ResultsDir,

    [string]$TestsBaseDir,

    [int]$TimeoutSeconds = 300,

    [Parameter(Mandatory)]
    [int]$MaxRetries,

    [Parameter(Mandatory)]
    [string]$RunId,

    [Parameter(Mandatory)]
    [string]$Model,

    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\")).Path
}

# Import helper functions
. (Join-Path $PSScriptRoot "invoke-copilot.ps1")

#region Helper Functions

function Parse-EvaluationJson {
    param([string]$Output)

    # Try to extract JSON from the output - it may be wrapped in markdown code blocks
    # Use a pattern that can match across lines for multi-line JSON
    $lines = $Output -split "`n"
    $jsonStartIdx = -1
    $jsonEndIdx = -1

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*\{' -and $lines[$i] -match '"score"') {
            # Single-line JSON
            $jsonCandidate = $lines[$i].Trim()
            try {
                $json = $jsonCandidate | ConvertFrom-Json
                return $json
            } catch { }
        }
        if ($lines[$i] -match '^\s*\{' -and $jsonStartIdx -eq -1) {
            $jsonStartIdx = $i
        }
        if ($jsonStartIdx -ge 0 -and $lines[$i] -match '^\s*\}') {
            $jsonEndIdx = $i
            $jsonCandidate = ($lines[$jsonStartIdx..$jsonEndIdx] -join "`n").Trim()
            if ($jsonCandidate -match '"score"') {
                try {
                    $json = $jsonCandidate | ConvertFrom-Json
                    return $json
                } catch {
                    $jsonStartIdx = -1
                    $jsonEndIdx = -1
                }
            }
        }
    }

    # Fallback: regex extraction
    if ($Output -match '(?s)\{[^{}]*"score"\s*:\s*\d[^{}]*\}') {
        try {
            $json = $Matches[0] | ConvertFrom-Json
            return $json
        } catch {
            Write-Warning "Failed to parse extracted JSON: $_"
        }
    }

    # Return a default failure evaluation
    Write-Warning "Could not parse evaluation output."
    return [PSCustomObject]@{
        score         = 0
        accuracy      = 0
        completeness  = 0
        actionability = 0
        clarity       = 0
        reasoning     = "Failed to parse evaluation response from Copilot"
    }
}

#endregion

#region Main Logic

Write-Host ""
Write-Host ("=" * 60)
Write-Host "[EVAL] Evaluating Test: $TestName"
Write-Host ("=" * 60)

$testResultsDir = Join-Path $ResultsDir $TestName $RunId

# Read expected output from test folder
if (-not $TestsBaseDir) {
    throw "TestsBaseDir is required."
}
if (-not [System.IO.Path]::IsPathRooted($TestsBaseDir)) {
    $TestsBaseDir = Join-Path $RepoRoot $TestsBaseDir
}
$testBaseDir = Join-Path $TestsBaseDir $TestName
$expectedFile = Join-Path $testBaseDir "expected-output.md"
if (-not (Test-Path $expectedFile)) {
    throw "Expected output file not found: $expectedFile"
}
Write-Host "[INFO] Expected output loaded from: $expectedFile"

# Process each run type - prepare eval dirs, then run concurrently
$runTypes = @("vanilla", "skilled")
$evaluations = @{}

# Evaluation instructions template (identical for all run types)
$instructions = @"
# Evaluation Task

Read the two files in this directory:
1. **expected-output.md** - The ground truth / expected findings
2. **actual-response.txt** - The AI assistant's actual response

## Step 1: Checklist Scoring (Primary Metric)
If expected-output.md contains an "## Evaluation Checklist" section, score each checklist item:
- Read each checklist item carefully
- Check if the actual response correctly identifies and addresses that item
- Award 1 point for each item that is correctly identified AND addressed with a reasonable solution
- Award 0.5 points for items partially addressed (identified but wrong/vague fix, or fix without identification)
- Award 0 points for items not mentioned or completely wrong
- Calculate: checklist_score = points_earned, checklist_max = total_items

If there is no Evaluation Checklist section, set checklist_score and checklist_max to null.

## Step 2: Subjective Quality Rating (Secondary Metric)
Rate the actual response on a scale of 0-10 where:
- 0-2: Major errors, misidentification of problems, harmful suggestions
- 3-4: Partially correct, misses major issues, vague solutions
- 5-6: Correct identification of main issues, but missing some, generic solutions
- 7-8: Identifies most issues correctly with specific, actionable solutions
- 9-10: Identifies all issues, provides expert-level solutions with precise MSBuild concepts

Use the full range. A response that catches 5/7 issues with correct fixes is a 7, not a 4.

Rate each dimension independently on the same 0-10 scale:
1. **Accuracy** - Did it correctly identify the problems without false positives?
2. **Completeness** - Did it find all issues mentioned in expected output?
3. **Actionability** - Are the suggested solutions practical, correct, and specific?
4. **Clarity** - Is the explanation clear and well-organized?

## Response Format
Your response must be ONLY a JSON object. Do not include any other text, markdown formatting, or code fences.

{"score": <0-10>, "accuracy": <0-10>, "completeness": <0-10>, "actionability": <0-10>, "clarity": <0-10>, "checklist_score": <number|null>, "checklist_max": <number|null>, "reasoning": "<brief explanation of scoring decisions>"}
"@

$evalPrompt = "Read the files in this directory: INSTRUCTIONS.md, expected-output.md, and actual-response.txt. Follow the instructions in INSTRUCTIONS.md to evaluate the actual response against the expected output. Your response must be ONLY a JSON object as specified in the instructions. Do not include any markdown code fences, explanatory text, or anything other than the raw JSON object."

# Phase 1: Prepare evaluation directories for each run type
$evalTasks = @()
foreach ($runType in $runTypes) {
    $outputFile = Join-Path $testResultsDir "${runType}-output.txt"

    if (-not (Test-Path $outputFile)) {
        Write-Warning "[WARN] Output file not found for $runType run: $outputFile"
        $evaluations[$runType] = [PSCustomObject]@{
            score         = 0
            accuracy      = 0
            completeness  = 0
            actionability = 0
            clarity       = 0
            reasoning     = "Output file not found - run may have failed or was skipped"
        }
        continue
    }

    $actualOutput = Get-Content $outputFile -Raw
    Write-Host ""
    Write-Host "[EVAL] Preparing $runType evaluation ($($actualOutput.Length) chars)..."

    $evalDir = Join-Path ([System.IO.Path]::GetTempPath()) "copilot-eval-${runType}-$(Get-Random)"
    New-Item -ItemType Directory -Force -Path $evalDir | Out-Null
    Copy-Item -Path $expectedFile -Destination (Join-Path $evalDir "expected-output.md")
    Copy-Item -Path $outputFile -Destination (Join-Path $evalDir "actual-response.txt")
    $instructions | Out-File -FilePath (Join-Path $evalDir "INSTRUCTIONS.md") -Encoding utf8

    $evalTasks += @{
        RunType = $runType
        EvalDir = $evalDir
    }
}

# Phase 2: Run evaluations concurrently
if ($evalTasks.Count -gt 0) {
    Write-Host ""
    Write-Host "[EVAL] Running $($evalTasks.Count) evaluation(s) concurrently..."

    $scriptRootPath = $PSScriptRoot

    $evalResults = $evalTasks | ForEach-Object -ThrottleLimit $evalTasks.Count -Parallel {
        $task = $_
        $runType = $task.RunType
        $evalDir = $task.EvalDir
        $retries = $using:MaxRetries
        $timeout = $using:TimeoutSeconds
        $mdl = $using:Model
        $prompt = $using:evalPrompt
        $resultsDir = $using:testResultsDir
        $sr = $using:scriptRootPath

        # Import helper function in this parallel runspace
        . (Join-Path $sr "invoke-copilot.ps1")

        $rawOutput = $null

        for ($attempt = 1; $attempt -le $retries; $attempt++) {
            Write-Host "[EVAL] Running Copilot evaluator for $runType (attempt $attempt/$retries)..."
            $output = Invoke-CopilotCli `
                -Prompt $prompt `
                -WorkingDir $evalDir `
                -TimeoutSeconds $timeout `
                -Model $mdl

            if ($null -eq $output -or $output.Trim() -eq '') {
                Write-Warning "[EVAL] Attempt ${attempt}: Copilot returned no output (timeout or error)"
                if ($attempt -lt $retries) {
                    $delay = 60 * $attempt
                    Write-Host "[EVAL] Waiting ${delay}s before retry..."
                    Start-Sleep -Seconds $delay
                }
                continue
            }

            # Basic validation: check for parseable JSON with a score field
            $valid = $false
            try {
                if ($output -match '(?s)\{[^{}]*"score"\s*:\s*\d[^{}]*\}') {
                    $null = $Matches[0] | ConvertFrom-Json
                    $valid = $true
                }
            } catch { }

            if ($valid) {
                $rawOutput = $output
                Write-Host "[EVAL] Successfully got valid evaluation on attempt $attempt for $runType"
                break
            }

            Write-Warning "[EVAL] Attempt ${attempt}: Failed to parse evaluation JSON for $runType"
            $output | Out-File -FilePath (Join-Path $resultsDir "${runType}-eval-raw-attempt${attempt}.txt") -Encoding utf8
            if ($attempt -lt $retries) {
                $delay = 60 * $attempt
                Write-Host "[EVAL] Waiting ${delay}s before retry..."
                Start-Sleep -Seconds $delay
            }
        }

        [PSCustomObject]@{
            RunType   = $runType
            RawOutput = $rawOutput
            EvalDir   = $evalDir
        }
    }

    # Phase 3: Parse results and cleanup
    foreach ($result in @($evalResults)) {
        $runType = $result.RunType

        if ($null -eq $result.RawOutput) {
            Write-Warning "[EVAL] All $MaxRetries attempts failed for $runType"
            $evaluations[$runType] = [PSCustomObject]@{
                score         = $null
                accuracy      = $null
                completeness  = $null
                actionability = $null
                clarity       = $null
                reasoning     = "All evaluation attempts failed (timeout or parse error)"
            }
        } else {
            $evalRawFile = Join-Path $testResultsDir "${runType}-eval-raw.txt"
            $result.RawOutput | Out-File -FilePath $evalRawFile -Encoding utf8

            $evaluation = Parse-EvaluationJson -Output $result.RawOutput
            $evaluations[$runType] = $evaluation
        }

        $e = $evaluations[$runType]
        Write-Host "   [$runType] Score: $($e.score)/10"
        Write-Host "   [$runType] Accuracy: $($e.accuracy)/10"
        Write-Host "   [$runType] Completeness: $($e.completeness)/10"
        Write-Host "   [$runType] Actionability: $($e.actionability)/10"
        Write-Host "   [$runType] Clarity: $($e.clarity)/10"
        if ($null -ne $e.checklist_score) {
            Write-Host "   [$runType] Checklist: $($e.checklist_score)/$($e.checklist_max)"
        }
        Write-Host "   [$runType] Reasoning: $($e.reasoning)"

        Remove-Item -Path $result.EvalDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Save combined evaluation results
$evalResult = @{
    test    = $TestName
    timestamp   = (Get-Date -Format "o")
    evaluations = $evaluations
}

$evalFile = Join-Path $testResultsDir "evaluation.json"
$evalResult | ConvertTo-Json -Depth 10 | Out-File -FilePath $evalFile -Encoding utf8

Write-Host ""
Write-Host "[OK] Evaluation complete for $TestName"
Write-Host "   Results saved to: $evalFile"

#endregion
