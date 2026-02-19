<#
.SYNOPSIS
    Runs tests in parallel with error propagation.

.DESCRIPTION
    Executes a PowerShell script for each test in parallel using
    ForEach-Object -Parallel, collects failures via ConcurrentBag, and
    throws if any test's script exited with a non-zero exit code.
    All tests run to completion before failing.

.PARAMETER Script
    Path to the per-test script (e.g., run-test.ps1 or evaluate-response.ps1).

.PARAMETER RunType
    Optional run type passed to the script (e.g., "vanilla" or "skilled").

.PARAMETER Tests
    Comma-separated list of test names.

.PARAMETER Parallelism
    Number of tests to run concurrently.

.PARAMETER Component
    Component name. Used to derive the tests base directory.

.PARAMETER ResultsDir
    Path to the results directory.

.PARAMETER RunId
    Unique identifier for this run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Script,

    [string]$RunType,

    [Parameter(Mandatory)]
    [string]$Tests,

    [Parameter(Mandatory)]
    [int]$Parallelism,

    [Parameter(Mandatory)]
    [string]$Component,

    [Parameter(Mandatory)]
    [string]$ResultsDir,

    [Parameter(Mandatory)]
    [string]$RunId,

    [Parameter(Mandatory)]
    [string]$Model,

    [Parameter(Mandatory)]
    [int]$MaxRetries
)

$ErrorActionPreference = "Stop"

$testList = ($Tests -split ",") | Where-Object { $_.Trim() -ne "" }
if ($testList.Count -eq 0) {
    throw "No tests provided. The tests list is empty."
}
$label = if ($RunType) { $RunType } else { "evaluation" }
$resolvedScript = (Resolve-Path $Script).Path
$testsBaseDir = "src/$Component/tests"

Write-Host "`nRunning $($testList.Count) tests ($label) with parallelism level: $Parallelism"

$failures = [System.Collections.Concurrent.ConcurrentBag[string]]::new()

$testList | ForEach-Object -ThrottleLimit $Parallelism -Parallel {
    $test = $_
    $script = $using:resolvedScript
    $resultsDir = $using:ResultsDir
    $runId = $using:RunId
    $runType = $using:RunType
    $testsBaseDir = $using:testsBaseDir
    $failures = $using:failures
    $label = $using:label
    $model = $using:Model
    $maxRetries = $using:MaxRetries

    Write-Host "`n=== ${label}: $test ==="

    $scriptArgs = @(
        "-TestName", $test,
        "-ResultsDir", $resultsDir,
        "-RunId", $runId,
        "-TestsBaseDir", $testsBaseDir,
        "-Model", $model,
        "-MaxRetries", $maxRetries
    )
    if ($runType) {
        $scriptArgs += @("-RunType", $runType)
    }

    # Capture output and prefix each line with the test name for readable logs
    $output = pwsh -File $script @scriptArgs 2>&1
    $exitCode = $LASTEXITCODE

    $output | ForEach-Object { Write-Host "[$test] $_" }

    if ($exitCode -ne 0) {
        $failures.Add($test)
        Write-Warning "[$test] Test $test ($label) failed with exit code $exitCode"
    }
}

if ($failures.Count -gt 0) {
    throw "❌ Test run completed. $($failures.Count)/$($testList.Count) $label test(s) failed: $($failures -join ', ')"
}
else {
    Write-Host "✅ Test run completed. All $label tests passed successfully."
}
