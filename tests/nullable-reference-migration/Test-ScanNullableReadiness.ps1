<#
.SYNOPSIS
    Automated tests for Scan-NullableReadiness.ps1.

.DESCRIPTION
    Runs the scanner against fixture projects with known NRT states
    and verifies the JSON output matches expected values.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$scannerPath = Join-Path $scriptDir "..\..\skills\nullable-reference-migration\scripts\Scan-NullableReadiness.ps1"
$fixturesDir = Join-Path $scriptDir "fixtures"

$passed = 0
$failed = 0

function Assert-Equal {
    param([string]$TestName, $Expected, $Actual)
    if ($Expected -ne $Actual) {
        $script:failed++
        Write-Host "  FAIL: $TestName — expected '$Expected', got '$Actual'" -ForegroundColor Red
    } else {
        $script:passed++
        Write-Host "  PASS: $TestName" -ForegroundColor Green
    }
}

function Assert-GreaterOrEqual {
    param([string]$TestName, [int]$Minimum, [int]$Actual)
    if ($Actual -lt $Minimum) {
        $script:failed++
        Write-Host "  FAIL: $TestName — expected >= $Minimum, got $Actual" -ForegroundColor Red
    } else {
        $script:passed++
        Write-Host "  PASS: $TestName ($Actual >= $Minimum)" -ForegroundColor Green
    }
}

# --- Test 1: nrt-disabled ---
Write-Host "`nTest: nrt-disabled" -ForegroundColor Cyan
$json = & $scannerPath -Path (Join-Path $fixturesDir "nrt-disabled\NrtDisabled.csproj") -Json | ConvertFrom-Json

Assert-Equal "Nullable is not set" "(not set)" $json.Nullable
Assert-Equal "Project name" "NrtDisabled" $json.Project
Assert-Equal "Total .cs files" 2 $json.TotalCsFiles
Assert-Equal "Files with #nullable enable" 0 $json.FilesWithEnable
Assert-GreaterOrEqual "Has #nullable disable" 1 $json.NullableDisable
Assert-GreaterOrEqual "Has ! operators" 2 $json.BangOperators
Assert-GreaterOrEqual "Has #pragma CS86xx" 1 $json.PragmaDisableCS86

# --- Test 2: nrt-enabled ---
Write-Host "`nTest: nrt-enabled" -ForegroundColor Cyan
$json = & $scannerPath -Path (Join-Path $fixturesDir "nrt-enabled\NrtEnabled.csproj") -Json | ConvertFrom-Json

Assert-Equal "Nullable is enable" "enable" $json.Nullable
Assert-Equal "Project name" "NrtEnabled" $json.Project
Assert-Equal "Total .cs files" 1 $json.TotalCsFiles
Assert-Equal "No #nullable disable" 0 $json.NullableDisable
Assert-Equal "No ! operators" 0 $json.BangOperators
Assert-Equal "No #pragma CS86xx" 0 $json.PragmaDisableCS86

# --- Test 3: nrt-partial ---
Write-Host "`nTest: nrt-partial" -ForegroundColor Cyan
$json = & $scannerPath -Path (Join-Path $fixturesDir "nrt-partial\NrtPartial.csproj") -Json | ConvertFrom-Json

Assert-Equal "Nullable is enable" "enable" $json.Nullable
Assert-Equal "Project name" "NrtPartial" $json.Project
Assert-Equal "Total .cs files" 2 $json.TotalCsFiles
Assert-GreaterOrEqual "Has #nullable disable" 1 $json.NullableDisable
Assert-GreaterOrEqual "Has ! operators" 1 $json.BangOperators
Assert-GreaterOrEqual "Has #pragma CS86xx" 1 $json.PragmaDisableCS86

# --- Summary ---
Write-Host "`n=== Results: $passed passed, $failed failed ===" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })

if ($failed -gt 0) {
    exit 1
}
