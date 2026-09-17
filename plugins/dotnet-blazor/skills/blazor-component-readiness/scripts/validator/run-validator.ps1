[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $ValidatorArguments
)

$ErrorActionPreference = 'Stop'
trap {
    [Console]::Error.WriteLine(
        "error: readiness launcher setup failed: $($_.Exception.Message)")
    exit 3
}

function Resolve-PhysicalDirectoryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [IO.Directory]::Exists($fullPath)) {
        throw "Directory does not exist: $fullPath"
    }

    $root = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($root)) {
        throw "Directory has no filesystem root: $fullPath"
    }

    $current = $root
    $relative = [IO.Path]::GetRelativePath($root, $fullPath)
    if ($relative -eq '.') {
        return [IO.Path]::GetFullPath($current)
    }

    $separators = [char[]] @(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    foreach ($segment in $relative.Split(
            $separators,
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $candidate = Join-Path $current $segment
        $item = Get-Item -Force -LiteralPath $candidate
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $target = $item.ResolveLinkTarget($true)
            if ($null -eq $target) {
                throw "Could not resolve symbolic-link directory: $candidate"
            }

            $current = Resolve-PhysicalDirectoryPath $target.FullName
        }
        else {
            $current = $item.FullName
        }
    }

    return [IO.Path]::GetFullPath($current)
}

$scriptDir = Resolve-PhysicalDirectoryPath $PSScriptRoot
$pluginRoot = Resolve-PhysicalDirectoryPath (Join-Path $scriptDir '../../../..')
$project = Join-Path $scriptDir 'BlazorComponentReadiness.Validator.csproj'
$restoreConfig = Join-Path $scriptDir 'restore-offline.config'

$sdkVersion = (& dotnet --version 2>$null)
if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^11\.') {
    $active = if ([string]::IsNullOrWhiteSpace($sdkVersion)) { 'unavailable' } else { $sdkVersion }
    [Console]::Error.WriteLine(
        "error: the readiness validator requires the repository-selected .NET 11 SDK; active SDK is $active")
    [Console]::Error.WriteLine('Select an installed .NET 11 SDK before running this launcher.')
    exit 3
}

$cleanup = $false
if ([string]::IsNullOrWhiteSpace($env:READINESS_TEMP)) {
    $tempParent = [IO.Path]::GetTempPath()
    if ([string]::IsNullOrWhiteSpace($tempParent)) {
        [Console]::Error.WriteLine(
            'error: no temporary directory is configured; set READINESS_TEMP.')
        exit 3
    }

    $readinessTemp = Join-Path $tempParent (
        'blazor-readiness-validator-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($readinessTemp) | Out-Null
    $cleanup = $true
}
else {
    $requestedTemp = [IO.Path]::GetFullPath($env:READINESS_TEMP).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $readinessParent = [IO.Path]::GetDirectoryName($requestedTemp)
    if ([string]::IsNullOrWhiteSpace($readinessParent) -or
        -not [IO.Directory]::Exists($readinessParent)) {
        [Console]::Error.WriteLine(
            "error: the parent of READINESS_TEMP must already exist: $readinessParent")
        exit 3
    }

    $physicalParent = Resolve-PhysicalDirectoryPath $readinessParent
    $readinessTemp = Join-Path $physicalParent ([IO.Path]::GetFileName($requestedTemp))
}

$comparison = if ($IsWindows) {
    [StringComparison]::OrdinalIgnoreCase
}
else {
    [StringComparison]::Ordinal
}
$pluginPrefix = $pluginRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (($readinessTemp + [IO.Path]::DirectorySeparatorChar).StartsWith(
        $pluginPrefix,
        $comparison)) {
    [Console]::Error.WriteLine('error: READINESS_TEMP must be outside the plugin tree.')
    exit 3
}

[IO.Directory]::CreateDirectory($readinessTemp) | Out-Null
$readinessTemp = Resolve-PhysicalDirectoryPath $readinessTemp
if (($readinessTemp + [IO.Path]::DirectorySeparatorChar).StartsWith(
        $pluginPrefix,
        $comparison)) {
    [Console]::Error.WriteLine('error: READINESS_TEMP resolves inside the plugin tree.')
    exit 3
}

[IO.Directory]::CreateDirectory((Join-Path $readinessTemp 'packages')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $readinessTemp 'bin')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $readinessTemp 'obj')) | Out-Null
foreach ($artifactDirectory in @('packages', 'bin', 'obj')) {
    $artifactPath = Join-Path $readinessTemp $artifactDirectory
    if (([IO.File]::GetAttributes($artifactPath) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        [Console]::Error.WriteLine(
            "error: external artifact directories cannot be symbolic links: $artifactPath")
        exit 3
    }
}

try {
    $common = @(
        '-noAutoResponse'
        '-property:Configuration=Release'
        '-property:ImportDirectoryBuildProps=false'
        '-property:ImportDirectoryBuildTargets=false'
        '-property:ImportDirectoryPackagesProps=false'
        "-property:RestoreConfigFile=$restoreConfig"
        "-property:RestorePackagesPath=$(Join-Path $readinessTemp 'packages')"
        '-property:NuGetAudit=false'
        "-property:BaseOutputPath=$(Join-Path $readinessTemp 'bin')$([IO.Path]::DirectorySeparatorChar)"
        "-property:BaseIntermediateOutputPath=$(Join-Path $readinessTemp 'obj')$([IO.Path]::DirectorySeparatorChar)"
    )

    & dotnet msbuild $project -target:Restore @common
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine('error: readiness validator restore failed.')
        exit 3
    }

    & dotnet msbuild $project '-target:VerifyNoPackageReferences;Build' @common
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine('error: readiness validator build failed.')
        exit 3
    }

    $validator = Join-Path $readinessTemp (
        'bin/Release/net11.0/BlazorComponentReadiness.Validator.dll')
    if (-not [IO.File]::Exists($validator) -or
        ([IO.File]::GetAttributes($validator) -band [IO.FileAttributes]::ReparsePoint)) {
        [Console]::Error.WriteLine(
            "error: the expected external validator DLL was not produced: $validator")
        exit 3
    }

    $previousSkillRoot = $env:READINESS_SKILL_ROOT
    $env:READINESS_SKILL_ROOT = [IO.Path]::GetFullPath((Join-Path $scriptDir '../..'))
    try {
        & dotnet $validator @ValidatorArguments
    }
    finally {
        $env:READINESS_SKILL_ROOT = $previousSkillRoot
    }
    $validatorExit = $LASTEXITCODE
    if ($validatorExit -in 0, 1, 2, 3) {
        exit $validatorExit
    }

    [Console]::Error.WriteLine(
        "error: readiness validator returned undocumented exit code $validatorExit.")
    exit 3
}
finally {
    if ($cleanup -and [IO.Directory]::Exists($readinessTemp)) {
        [IO.Directory]::Delete($readinessTemp, $true)
    }
}
