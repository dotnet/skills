Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$analyzer = Join-Path $repoRoot "plugins\dotnet-test\skills\find-untested-sources\scripts\Find-UntestedSources.cs"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("dotnet-skills-find-untested-" + [Guid]::NewGuid().ToString("N"))
$repositoryRoot = Join-Path $tempRoot "repo"
$outsideSourceRoot = Join-Path $tempRoot "outside-source"
$outsideTestRoot = Join-Path $tempRoot "outside-test"
$sourceLink = Join-Path $repositoryRoot "linked-source"
$testProjectLink = Join-Path $repositoryRoot "linked-tests"
$passed = 0
$failed = 0
$skipped = 0

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

function Assert-Contains {
    param([string]$TestName, [string]$Expected, [string[]]$Actual)

    if (@($Actual) -cnotcontains $Expected) {
        $script:failed++
        Write-Host "FAIL: $TestName - missing '$Expected'" -ForegroundColor Red
    } else {
        $script:passed++
        Write-Host "PASS: $TestName" -ForegroundColor Green
    }
}

function Add-Skip {
    param([string]$TestName)

    $script:skipped++
    Write-Host "SKIP: $TestName - directory links are unavailable" -ForegroundColor Yellow
}

function Write-TestFile {
    param([string]$Root, [string]$RelativePath, [string]$Content)

    $path = Join-Path $Root $RelativePath
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
    [IO.File]::WriteAllText($path, $Content)
}

function Try-CreateDirectoryLink {
    param([string]$LinkPath, [string]$TargetPath)

    try {
        [IO.Directory]::CreateSymbolicLink($LinkPath, $TargetPath) | Out-Null
        return $true
    } catch {
        if ([OperatingSystem]::IsWindows()) {
            try {
                New-Item -ItemType Junction -Path $LinkPath -Target $TargetPath -ErrorAction Stop | Out-Null
                return $true
            } catch {
                return $false
            }
        }
    }
    return $false
}

function Get-PairedTests {
    param($Result, [string]$SourcePath)

    $property = $Result.source_to_tests.PSObject.Properties[$SourcePath]
    if ($null -eq $property) {
        return @()
    }
    return @($property.Value)
}

[IO.Directory]::CreateDirectory($repositoryRoot) | Out-Null
[IO.Directory]::CreateDirectory($outsideSourceRoot) | Out-Null
[IO.Directory]::CreateDirectory($outsideTestRoot) | Out-Null

try {
    Write-TestFile $repositoryRoot "src\App\App.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
'@
    Write-TestFile $repositoryRoot "src\App\ExactType.cs" @'
namespace Visibility;
public sealed class ExactType { }
'@
    Write-TestFile $repositoryRoot "src\App\ChildType.cs" @'
namespace Visibility.Child;
public sealed class ChildType { }
'@
    Write-TestFile $repositoryRoot "src\App\EnclosingType.cs" @'
namespace Enclosing;
public sealed class EnclosingType { }
'@
    Write-TestFile $repositoryRoot "tests\App.Tests\App.Tests.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
</Project>
'@
    Write-TestFile $repositoryRoot "tests\App.Tests\ImportTests.cs" @'
using Visibility;
namespace Consumer.Tests;
public sealed class ImportTests
{
    private ExactType? exact;
    private ChildType? child;
}
'@
    Write-TestFile $repositoryRoot "tests\App.Tests\EnclosingTests.cs" @'
namespace Enclosing.Tests;
public sealed class EnclosingTests
{
    private EnclosingType? value;
}
'@

    Write-TestFile $outsideSourceRoot "Outside.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
'@
    Write-TestFile $outsideSourceRoot "LinkedType.cs" @'
namespace Outside;
public sealed class LinkedType { }
'@
    Write-TestFile $outsideTestRoot "Outside.Tests.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\App\App.csproj" />
  </ItemGroup>
</Project>
'@

    $sourceLinkCreated = Try-CreateDirectoryLink $sourceLink $outsideSourceRoot
    $testProjectLinkCreated = Try-CreateDirectoryLink $testProjectLink $outsideTestRoot

    $stderrPath = Join-Path $tempRoot "analyzer.stderr.txt"
    Push-Location $tempRoot
    try {
        $stdout = @(& dotnet run --file $analyzer -- $repositoryRoot 2> $stderrPath)
    } finally {
        Pop-Location
    }
    if ($LASTEXITCODE -ne 0) {
        $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -Raw $stderrPath } else { "" }
        throw "Find-UntestedSources failed with exit code $LASTEXITCODE`n$stderr"
    }
    $result = ($stdout -join [Environment]::NewLine) | ConvertFrom-Json

    Assert-Contains "Exact namespace imports remain visible" "tests/App.Tests/ImportTests.cs" (Get-PairedTests $result "src/App/ExactType.cs")
    Assert-Contains "Enclosing namespaces remain visible" "tests/App.Tests/EnclosingTests.cs" (Get-PairedTests $result "src/App/EnclosingType.cs")

    $childType = @($result.untested | Where-Object { $_.source -ceq "src/App/ChildType.cs" })
    Assert-Equal "Parent imports do not import child namespaces" 1 $childType.Count

    if ($sourceLinkCreated) {
        Assert-Equal "Source discovery skips directory reparse points" 3 ([int]$result.counts.source_files)
        Assert-Equal "Linked outside sources are absent" 0 @($result.untested | Where-Object { $_.source -like "linked-source/*" }).Count
    } else {
        Add-Skip "Source discovery skips directory reparse points"
    }

    if ($testProjectLinkCreated) {
        Assert-Equal "Project discovery skips directory reparse points" $null $childType[0].suggested_test_path
    } else {
        Add-Skip "Project discovery skips directory reparse points"
    }
} finally {
    foreach ($link in @($sourceLink, $testProjectLink)) {
        if (Test-Path -LiteralPath $link) {
            Remove-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
        }
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

Write-Host "Results: $passed passed, $failed failed, $skipped skipped"
if ($failed -gt 0) {
    exit 1
}
