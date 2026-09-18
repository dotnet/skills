Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$computeScript = Join-Path $repoRoot "plugins\dotnet-test\skills\coverage-analysis\scripts\Compute-CrapScores.ps1"
$extractScript = Join-Path $repoRoot "plugins\dotnet-test\skills\coverage-analysis\scripts\Extract-MethodCoverage.ps1"
$methodOnlyReport = Join-Path $repoRoot "tests\dotnet-test\coverage-analysis\fixtures\partial-coverage\coverage.cobertura.xml"
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("dotnet-skills-coverage-" + [Guid]::NewGuid().ToString("N"))
$partialReport = Join-Path $tempRoot "partial.cobertura.xml"
$fullReport = Join-Path $tempRoot "full.cobertura.xml"
$passed = 0
$failed = 0

function Assert-Equal {
    param([string]$TestName, $Expected, $Actual)

    if ($Expected -ne $Actual) {
        $script:failed++
        Write-Host "FAIL: $TestName - expected '$Expected', got '$Actual'" -ForegroundColor Red
    } else {
        $script:passed++
        Write-Host "PASS: $TestName" -ForegroundColor Green
    }
}

function Assert-ContainsLine {
    param([string]$TestName, [string]$Expected, [string[]]$Actual)

    if (@($Actual) -cnotcontains $Expected) {
        $script:failed++
        Write-Host "FAIL: $TestName - missing '$Expected'" -ForegroundColor Red
    } else {
        $script:passed++
        Write-Host "PASS: $TestName" -ForegroundColor Green
    }
}

function Invoke-CoverageScript {
    param(
        [string]$ScriptPath,
        [string[]]$CoberturaPaths,
        [switch]$IncludeFilter
    )

    $previousScript = $env:DOTNET_SKILLS_TEST_SCRIPT
    $previousPaths = $env:DOTNET_SKILLS_TEST_PATHS
    try {
        $env:DOTNET_SKILLS_TEST_SCRIPT = $ScriptPath
        $env:DOTNET_SKILLS_TEST_PATHS = ConvertTo-Json -Compress -InputObject ([object[]]$CoberturaPaths)
        $command = '$paths = @($env:DOTNET_SKILLS_TEST_PATHS | ConvertFrom-Json); & $env:DOTNET_SKILLS_TEST_SCRIPT -CoberturaPath $paths'
        if ($IncludeFilter) {
            $command += ' -Filter all'
        }
        $output = @(& $pwsh -NoLogo -NoProfile -NonInteractive -Command $command 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        $env:DOTNET_SKILLS_TEST_SCRIPT = $previousScript
        $env:DOTNET_SKILLS_TEST_PATHS = $previousPaths
    }

    if ($exitCode -ne 0) {
        throw "Coverage script failed with exit code $exitCode`n$($output -join [Environment]::NewLine)"
    }
    return @($output | ForEach-Object { $_.ToString() })
}

function ConvertFrom-MethodOutput {
    param([string[]]$Output)

    $summaryIndex = -1
    for ($i = 0; $i -lt $Output.Count; $i++) {
        if ($Output[$i] -match "METHODS_FILTERED:") {
            $summaryIndex = $i
            break
        }
    }
    if ($summaryIndex -lt 0) {
        throw "Method coverage output did not contain METHODS_FILTERED."
    }
    $json = $Output[0..($summaryIndex - 1)] -join [Environment]::NewLine
    return @($json | ConvertFrom-Json)
}

[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
try {
    [IO.File]::WriteAllText($partialReport, @'
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.5" branch-rate="0.5" lines-covered="1" lines-valid="2" branches-covered="1" branches-valid="2">
  <packages>
    <package name="Demo">
      <classes>
        <class name="Demo.Sample" filename="src/Sample.cs">
          <methods>
            <method name="Run" signature="()" complexity="2">
              <lines>
                <line number="10" hits="1" branch="true" condition-coverage="50% (1/2)" />
              </lines>
            </method>
            <method name="run" signature="()" complexity="1">
              <lines>
                <line number="11" hits="0" />
              </lines>
            </method>
          </methods>
          <lines>
            <line number="10" hits="1" branch="true" condition-coverage="50% (1/2)" />
            <line number="11" hits="0" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
'@)
    [IO.File]::WriteAllText($fullReport, @'
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="1.0" branch-rate="1.0" lines-covered="2" lines-valid="2" branches-covered="2" branches-valid="2">
  <packages>
    <package name="Demo">
      <classes>
        <class name="Demo.Sample" filename="src/Sample.cs">
          <methods>
            <method name="Run" signature="()" complexity="2">
              <lines>
                <line number="10" hits="1" branch="true" condition-coverage="100% (2/2)" />
              </lines>
            </method>
            <method name="run" signature="()" complexity="1">
              <lines>
                <line number="11" hits="1" />
              </lines>
            </method>
          </methods>
          <lines>
            <line number="10" hits="1" branch="true" condition-coverage="100% (2/2)" />
            <line number="11" hits="1" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
'@)

    $computeOutput = @(Invoke-CoverageScript -ScriptPath $computeScript -CoberturaPaths @($partialReport, $fullReport))
    Assert-ContainsLine "Overlapping lines use file-and-line identity" "OVERALL_LINE_COVERAGE:100" $computeOutput
    Assert-ContainsLine "Overlapping branches are not counted twice" "OVERALL_BRANCH_COVERAGE:100" $computeOutput
    Assert-ContainsLine "Case-distinct methods remain distinct in CRAP output" "TOTAL_METHODS:2" $computeOutput

    $combinedMethods = @(ConvertFrom-MethodOutput (Invoke-CoverageScript -ScriptPath $extractScript -CoberturaPaths @($partialReport, $fullReport) -IncludeFilter))
    Assert-Equal "Method extraction preserves case-distinct identities" 2 $combinedMethods.Count
    Assert-Equal "Uppercase method remains present" $true (@($combinedMethods.Method) -ccontains "Run")
    Assert-Equal "Lowercase method remains present" $true (@($combinedMethods.Method) -ccontains "run")

    $duplicateMethods = @(ConvertFrom-MethodOutput (Invoke-CoverageScript -ScriptPath $extractScript -CoberturaPaths @($partialReport, $partialReport) -IncludeFilter))
    $runMethod = @($duplicateMethods | Where-Object { $_.Method -ceq "Run" })
    Assert-Equal "Duplicate reports retain one uppercase method" 1 $runMethod.Count
    Assert-Equal "Duplicate partial branch coverage stays partial" 50 ([double]$runMethod[0].BranchCoverage)
    Assert-Equal "Duplicate partial branch count is not inflated" 1 ([int]$runMethod[0].CoveredBranches)

    $methodOnlyOutput = @(Invoke-CoverageScript -ScriptPath $computeScript -CoberturaPaths @($methodOnlyReport))
    Assert-ContainsLine "Method lines backfill missing class lines" "OVERALL_LINE_COVERAGE:46.8" $methodOnlyOutput
    Assert-ContainsLine "Method branches backfill missing class lines" "OVERALL_BRANCH_COVERAGE:43.8" $methodOnlyOutput
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

Write-Host "Results: $passed passed, $failed failed"
if ($failed -gt 0) {
    exit 1
}
