<#
.SYNOPSIS
    Automated tests for Get-NullableReadiness.ps1.

.DESCRIPTION
    Runs the scanner against fixture projects with known NRT states
    and verifies the JSON output matches expected values.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$scannerPath = Join-Path $scriptDir ".." ".." "skills" "migrate-nullable-references" "scripts" "Get-NullableReadiness.ps1"
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
# Formatter.cs has 4 real ! operators: result! and ToString()! (assertions) plus
# null! and default! (initializers). Also has ! in strings, comments, and XML docs.
# UserService.cs has 1 real ! operator (LastName!) plus #nullable disable and #pragma.
# The scanner must NOT count ! inside strings ("Formatted!", "Value required!"),
# comments (// Important! ... null!), XML docs (input! ... failure!), or block comments (non-null!).
Write-Host "`nTest: nrt-disabled" -ForegroundColor Cyan
$json = & $scannerPath -Path (Join-Path $fixturesDir "nrt-disabled" "NrtDisabled.csproj") -Json | ConvertFrom-Json

Assert-Equal "Nullable is not set" "(not set)" $json.Nullable
Assert-Equal "Project name" "NrtDisabled" $json.Project
Assert-Equal "Total .cs files" 2 $json.TotalCsFiles
Assert-Equal "Files with #nullable enable" 0 $json.FilesWithEnable
Assert-Equal "#nullable disable count" 1 $json.NullableDisable
Assert-Equal "! operators (excludes strings/comments)" 5 $json.BangOperators
Assert-Equal "null!/default! initializers" 2 $json.BangNullInit
Assert-Equal "! assertions" 3 $json.BangAssertions
Assert-Equal "#pragma CS86xx count" 1 $json.PragmaDisableCS86
Assert-Equal "Uninit ref fields (CS8618 estimate)" 3 $json.UninitFields

# --- Test 2: nrt-enabled ---
# UserService.cs has 0 real ! operators but has ! in a comment, XML doc,
# interpolated string ($"Log: {message}!"), and regular string ("Done!").
# The scanner must report exactly 0.
Write-Host "`nTest: nrt-enabled" -ForegroundColor Cyan
$json = & $scannerPath -Path (Join-Path $fixturesDir "nrt-enabled" "NrtEnabled.csproj") -Json | ConvertFrom-Json

Assert-Equal "Nullable is enable" "enable" $json.Nullable
Assert-Equal "Project name" "NrtEnabled" $json.Project
Assert-Equal "Total .cs files" 1 $json.TotalCsFiles
Assert-Equal "No #nullable disable" 0 $json.NullableDisable
Assert-Equal "No ! operators (strings/comments ignored)" 0 $json.BangOperators
Assert-Equal "No null!/default! initializers" 0 $json.BangNullInit
Assert-Equal "No ! assertions" 0 $json.BangAssertions
Assert-Equal "No #pragma CS86xx" 0 $json.PragmaDisableCS86
Assert-Equal "Uninit ref fields (required excluded)" 1 $json.UninitFields

# --- Test 3: nrt-partial ---
# LegacyHelper.cs has 1 real ! operator (ToString()!) plus ! in a comment and a block comment.
# CleanFile.cs has 0 ! operators.
# The scanner must NOT count the comment/block-comment bangs.
Write-Host "`nTest: nrt-partial" -ForegroundColor Cyan
$json = & $scannerPath -Path (Join-Path $fixturesDir "nrt-partial" "NrtPartial.csproj") -Json | ConvertFrom-Json

Assert-Equal "Nullable is enable" "enable" $json.Nullable
Assert-Equal "Project name" "NrtPartial" $json.Project
Assert-Equal "Total .cs files" 2 $json.TotalCsFiles
Assert-Equal "#nullable disable count" 1 $json.NullableDisable
Assert-Equal "! operators (excludes comments)" 1 $json.BangOperators
Assert-Equal "null!/default! initializers" 0 $json.BangNullInit
Assert-Equal "! assertions" 1 $json.BangAssertions
Assert-Equal "#pragma CS86xx count" 1 $json.PragmaDisableCS86
Assert-Equal "Uninit ref fields" 1 $json.UninitFields

# --- Summary ---
Write-Host "`n=== Results: $passed passed, $failed failed ===" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })

if ($failed -gt 0) {
    exit 1
}
