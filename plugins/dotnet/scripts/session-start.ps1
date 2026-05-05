#Requires -Version 5.1
<#
.SYNOPSIS
    Initializes the development session by ensuring the roslyn-language-server tool is installed and up to date.
.DESCRIPTION
    Creates a temporary working directory for the session and installs or updates the
    roslyn-language-server .NET global tool to the latest prerelease version.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ToolName = 'roslyn-language-server'
$NuGetSource = 'https://api.nuget.org/v3/index.json'

function New-SessionDirectory {
    $dir = New-Item -ItemType Directory -Path (Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString()))
    Write-Verbose "[$ToolName] Created temp directory: $($dir.FullName)"
    Set-Location -Path $dir.FullName
    return $dir
}

function Write-GlobalJson {
    param([string]$Directory)

    @{
        sdk = @{
            version     = '10.0.100'
            rollForward = 'latestMajor'
        }
    } | ConvertTo-Json -Depth 3 | Set-Content -Path (Join-Path $Directory 'global.json') -Encoding utf8
    Write-Verbose "[$ToolName] Wrote global.json pinning SDK to 10.0.100 (rollForward: latestMajor)"
}

function Get-InstalledToolVersion {
    param([string]$Name)

    $toolList = dotnet tool list --global
    $installedEntry = $toolList | Select-String -Pattern $Name
    if ($installedEntry) { ($installedEntry -split '\s+')[1] } else { $null }
}

function Get-LatestToolVersion {
    param([string]$Name, [string]$Source)

    $searchJson = dotnet package search $Name --source $Source --exact-match --prerelease --format json | ConvertFrom-Json
    $packages = $searchJson.searchResult | ForEach-Object { $_.packages } | Where-Object { $_.id -eq $Name }
    if ($packages) { ($packages | Select-Object -Last 1).version } else { $null }
}

function Install-OrUpdateTool {
    $currentVersion = Get-InstalledToolVersion -Name $ToolName

    if ($currentVersion) {
        Write-Host "[$ToolName] Installed version: $currentVersion"

        $latestVersion = Get-LatestToolVersion -Name $ToolName -Source $NuGetSource

        if (-not $latestVersion) {
            Write-Warning "[$ToolName] Unable to determine the latest version. Skipping update."
            return
        }

        Write-Host "[$ToolName] Latest available version: $latestVersion"

        if ($currentVersion -ne $latestVersion) {
            Write-Host "[$ToolName] Updating from $currentVersion to $latestVersion..."
            dotnet tool update --global --prerelease $ToolName --add-source $NuGetSource
            Write-Host "[$ToolName] Update complete."
        } else {
            Write-Host "[$ToolName] Already up to date."
        }
    } else {
        Write-Host "[$ToolName] Not found. Installing latest prerelease..."
        dotnet tool install --global --prerelease $ToolName --add-source $NuGetSource
        Write-Host "[$ToolName] Installation complete."
    }
}

$tempDir = New-SessionDirectory
Write-GlobalJson -Directory $tempDir.FullName
Install-OrUpdateTool
