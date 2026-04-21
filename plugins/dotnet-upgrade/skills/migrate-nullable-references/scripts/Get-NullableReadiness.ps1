<#
.SYNOPSIS
    Scans a C# project or solution for nullable reference type (NRT) readiness.

.DESCRIPTION
    Reports project-level NRT settings (<Nullable>, <LangVersion>, <TargetFramework>,
    <WarningsAsErrors>) and source-level counts (#nullable directives, null-forgiving
    operators, #pragma warning disable CS86xx) to help assess migration status.

    Automates the manual checks in Steps 1 and 6 of the migrate-nullable-references skill.

.PARAMETER Path
    Path to a .csproj, .sln, or directory. Defaults to the current directory.

.PARAMETER Json
    Output as JSON instead of a human-readable summary.

.PARAMETER Recurse
    When Path is a directory (not a .sln), scan recursively for all .csproj files.

.EXAMPLE
    ./Get-NullableReadiness.ps1
    Scans the current directory for a .sln or .csproj and reports NRT readiness.

.EXAMPLE
    ./Get-NullableReadiness.ps1 -Path ./src/MyLib/MyLib.csproj
    Scans a single project.

.EXAMPLE
    ./Get-NullableReadiness.ps1 -Path ./src -Recurse -Json
    Scans all projects under ./src and outputs JSON.

.NOTES
    Example output BEFORE NRT migration:

    === NRT Readiness Report ===
    Project: System.Text.RegularExpressions
      Path:               src\System.Text.RegularExpressions.csproj
      <Nullable>:         (not set)
      <LangVersion>:      latest (inherited)
      <TargetFramework>:  (not set)
      Warning enforcement: all warnings as errors
      Source files:       39
      #nullable enable:   1
      #nullable disable:  0
      #pragma CS86xx:     0
      ! operators (approx): 0
      Uninit ref fields:  ~322 (estimated CS8618 warnings)
      Migration progress: 1/39 files (2.6%)
      Migration work needed:
        CaptureCollection.cs: ~6 uninit fields
        GroupCollection.cs: ~9 uninit fields
        ....
    === Summary ===
      Projects scanned:   1
      NRT enabled:        0/1
      Total .cs files:    39
      Total #nullable disable: 0
      Total #pragma CS86xx:    0
      Total ! operators:       0
      Total uninit ref fields: ~322 (estimated CS8618 warnings)

    Example output AFTER NRT migration (same project, all 502 CS86xx warnings resolved):

    === NRT Readiness Report ===
    Project: System.Text.RegularExpressions
      Path:               src\System.Text.RegularExpressions.csproj
      <Nullable>:         enable
      <LangVersion>:      latest (inherited)
      <TargetFramework>:  (not set)
      Warning enforcement: all warnings as errors
      Source files:       39
      #nullable enable:   1
      #nullable disable:  0
      #pragma CS86xx:     0
      ! operators (approx): 192
        null!/default!:     47
        assertions:         145
      Suppression audit (review ! operators for possible removal):
        Match.cs: 7 !
        Regex.Cache.cs: 22 !
        Regex.cs: 11 !
        RegexCompiler.cs: 31 ! (24 null!/default!, 7 assertions)
        ...
    === Summary ===
      Projects scanned:   1
      NRT enabled:        1/1
      Total .cs files:    39
      Total #nullable disable: 0
      Total #pragma CS86xx:    0
      Total ! operators:       192
        null!/default!:        47
        assertions:            145
#>

[CmdletBinding()]
param(
    [string]$Path = ".",
    [switch]$Json,
    [switch]$Recurse
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

#region Helpers

function Get-ProjectFiles {
    param([string]$InputPath, [switch]$Recurse)

    $resolved = Resolve-Path $InputPath -ErrorAction Stop

    if (Test-Path $resolved -PathType Leaf) {
        $ext = [System.IO.Path]::GetExtension($resolved)
        if ($ext -eq ".csproj") {
            return @($resolved.Path)
        }
        if ($ext -eq ".sln") {
            return Get-ProjectsFromSolution $resolved.Path
        }
        Write-Error "Unsupported file type: $ext. Provide a .csproj, .sln, or directory."
    }

    # Directory
    if ($Recurse) {
        $projects = Get-ChildItem -Path $resolved -Filter "*.csproj" -Recurse | Select-Object -ExpandProperty FullName
    } else {
        # Look for .sln first, then .csproj in the directory
        $sln = Get-ChildItem -Path $resolved -Filter "*.sln" -File | Select-Object -First 1
        if ($sln) {
            return Get-ProjectsFromSolution $sln.FullName
        }
        $projects = Get-ChildItem -Path $resolved -Filter "*.csproj" -File | Select-Object -ExpandProperty FullName
    }

    if (-not $projects -or $projects.Count -eq 0) {
        Write-Error "No .csproj files found in '$resolved'."
    }
    return $projects
}

function Get-ProjectsFromSolution {
    param([string]$SlnPath)

    $slnDir = Split-Path $SlnPath -Parent
    $projects = @()
    foreach ($line in Get-Content $SlnPath) {
        if ($line -match 'Project\("[^"]*"\)\s*=\s*"[^"]*"\s*,\s*"([^"]*\.csproj)"') {
            $relPath = $Matches[1] -replace '\\', [System.IO.Path]::DirectorySeparatorChar
            $fullPath = Join-Path $slnDir $relPath
            if (Test-Path $fullPath) {
                $projects += (Resolve-Path $fullPath).Path
            }
        }
    }
    return $projects
}

function Read-ProjectSettings {
    param([string]$CsprojPath)

    $xml = [xml](Get-Content $CsprojPath -Raw)
    $ns = $xml.DocumentElement.NamespaceURI

    # Check for Directory.Build.props in parent directories
    $propsSettings = Find-DirectoryBuildProps (Split-Path $CsprojPath -Parent)

    $nullable = Select-XmlValue $xml "//Nullable" $ns
    $langVersion = Select-XmlValue $xml "//LangVersion" $ns
    $tfm = Select-XmlValue $xml "//TargetFramework" $ns
    $tfms = Select-XmlValue $xml "//TargetFrameworks" $ns
    $warningsAsErrors = Select-XmlValue $xml "//WarningsAsErrors" $ns
    $treatWarningsAsErrors = Select-XmlValue $xml "//TreatWarningsAsErrors" $ns

    # Fall back to Directory.Build.props values
    if (-not $nullable -and $propsSettings.Nullable) { $nullable = $propsSettings.Nullable + " (inherited)" }
    if (-not $langVersion -and $propsSettings.LangVersion) { $langVersion = $propsSettings.LangVersion + " (inherited)" }
    if (-not $warningsAsErrors -and $propsSettings.WarningsAsErrors) { $warningsAsErrors = $propsSettings.WarningsAsErrors + " (inherited)" }
    if (-not $treatWarningsAsErrors -and $propsSettings.TreatWarningsAsErrors) { $treatWarningsAsErrors = $propsSettings.TreatWarningsAsErrors + " (inherited)" }

    $framework = if ($tfms) { $tfms } elseif ($tfm) { $tfm } else { "(not set)" }

    $warningEnforcement = "none"
    if ($treatWarningsAsErrors -and $treatWarningsAsErrors -match "true") {
        $warningEnforcement = "all warnings as errors"
    } elseif ($warningsAsErrors -and $warningsAsErrors -match "nullable") {
        $warningEnforcement = "nullable warnings as errors"
    }

    return [PSCustomObject]@{
        Nullable           = if ($nullable) { $nullable } else { "(not set)" }
        LangVersion        = if ($langVersion) { $langVersion } else { "(not set)" }
        TargetFramework    = $framework
        WarningEnforcement = $warningEnforcement
    }
}

function Select-XmlValue {
    param($Xml, [string]$XPath, [string]$Namespace)

    if ($Namespace) {
        $nsmgr = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
        $nsmgr.AddNamespace("ns", $Namespace)
        $nsXPath = $XPath -replace '//', '//ns:' -replace '/ns:ns:', '/ns:'
        $node = $Xml.SelectSingleNode($nsXPath, $nsmgr)
    } else {
        $node = $Xml.SelectSingleNode($XPath)
    }

    if ($node) { return $node.InnerText.Trim() }
    return $null
}

function Find-DirectoryBuildProps {
    param([string]$StartDir)

    $result = [PSCustomObject]@{
        Nullable            = $null
        LangVersion         = $null
        WarningsAsErrors    = $null
        TreatWarningsAsErrors = $null
    }

    $dir = $StartDir
    while ($dir) {
        $propsPath = Join-Path $dir "Directory.Build.props"
        if (Test-Path $propsPath) {
            $xml = [xml](Get-Content $propsPath -Raw)
            $ns = $xml.DocumentElement.NamespaceURI
            if (-not $result.Nullable) { $result.Nullable = Select-XmlValue $xml "//Nullable" $ns }
            if (-not $result.LangVersion) { $result.LangVersion = Select-XmlValue $xml "//LangVersion" $ns }
            if (-not $result.WarningsAsErrors) { $result.WarningsAsErrors = Select-XmlValue $xml "//WarningsAsErrors" $ns }
            if (-not $result.TreatWarningsAsErrors) { $result.TreatWarningsAsErrors = Select-XmlValue $xml "//TreatWarningsAsErrors" $ns }
        }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }

    return $result
}

function Scan-SourceFiles {
    param([string]$CsprojPath)

    $projectDir = Split-Path $CsprojPath -Parent
    $csFiles = @(Get-ChildItem -Path $projectDir -Filter "*.cs" -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })

    $totalFiles = $csFiles.Count
    $filesWithNullableEnable = 0
    $totalNullableDisable = 0
    $totalNullableEnable = 0
    $totalPragmaDisable = 0
    $totalBangOperator = 0
    $totalBangNullInit = 0
    $totalBangAssertions = 0
    $totalUninitFields = 0
    $fileDetails = @()

    foreach ($file in $csFiles) {
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $content) { continue }

        $lines = $content -split '\r?\n'

        $nullableDisable = @($lines | Where-Object { $_ -match '^\s*#nullable\s+disable' }).Count
        $nullableEnable = @($lines | Where-Object { $_ -match '^\s*#nullable\s+enable' }).Count
        $pragmaDisable = @($lines | Where-Object { $_ -match '#pragma\s+warning\s+disable\s+CS86' }).Count

        # Count null-forgiving operators (approximate).
        # Strip string literals before comments to avoid false positives — a string
        # like "http://..." contains // that would otherwise be mis-parsed as a comment.
        # Then match ! preceded by ), ], >, or a word character, not followed by =.
        $strippedContent = $content
        $strippedContent = [regex]::Replace($strippedContent, '(?<!\$)@"([^"]|"")*"', '""')       # verbatim strings
        $strippedContent = [regex]::Replace($strippedContent, '\$"([^"\\]|\\.|\{[^}]*\})*"', '""') # interpolated strings
        $strippedContent = [regex]::Replace($strippedContent, '"([^"\\]|\\.)*"', '""')             # regular strings
        $strippedContent = [regex]::Replace($strippedContent, '/\*[\s\S]*?\*/', '')               # block comments
        $strippedContent = [regex]::Replace($strippedContent, '//[^\n]*', '')                     # line comments
        $bangMatches = [regex]::Matches($strippedContent, '(?<=[)\]>\w])!(?!=)')
        $bangCount = $bangMatches.Count

        # Categorize: null! initializers (= null!, => null!, default!) vs other assertions
        $nullInitCount = ([regex]::Matches($strippedContent, '(?:=\s*null!|=>\s*null!|default!)')).Count
        $bangAssertionCount = $bangCount - $nullInitCount

        # Estimate uninitialised reference-type fields and auto-properties (approximate CS8618 predictor).
        # Matches field declarations ending in ; without an = initializer, and auto-properties
        # without initializers, excluding value types and events.
        $valueTypes = 'bool|byte|sbyte|char|decimal|double|float|int|uint|long|ulong|short|ushort|nint|nuint|void|IntPtr|UIntPtr|Guid|DateTime|DateTimeOffset|TimeSpan|CancellationToken'
        $uninitFields = @($lines | Where-Object {
            (
                # Field declarations: type name;
                ($_ -match '^\s*(private|protected|internal|public|static|readonly|\s)+\s+\w[\w<>\[\],\?\.]*\s+\w+\s*;') -or
                # Auto-properties: type Name { get; set; } or { get; }
                ($_ -match '^\s*(private|protected|internal|public|static|virtual|override|abstract|\s)+\s+\w[\w<>\[\],\?\.]*\s+\w+\s*\{\s*get;')
            ) -and
            $_ -notmatch '=' -and
            $_ -notmatch '\brequired\b' -and
            $_ -notmatch "^\s*(private|protected|internal|public|static|readonly|virtual|override|abstract|\s)+\s+($valueTypes)\b" -and
            $_ -notmatch '^\s*(private|protected|internal|public|static|readonly|\s)+\s*(const|event)\b'
        }).Count

        if ($nullableEnable -gt 0) { $filesWithNullableEnable++ }
        $totalNullableDisable += $nullableDisable
        $totalNullableEnable += $nullableEnable
        $totalPragmaDisable += $pragmaDisable
        $totalBangOperator += $bangCount
        $totalBangNullInit += $nullInitCount
        $totalBangAssertions += $bangAssertionCount
        $totalUninitFields += $uninitFields

        $relativePath = $file.FullName.Substring($projectDir.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)

        if ($nullableDisable -gt 0 -or $pragmaDisable -gt 0 -or $bangCount -gt 5 -or $uninitFields -gt 5) {
            $fileDetails += [PSCustomObject]@{
                File            = $relativePath
                NullableDisable = $nullableDisable
                PragmaDisable   = $pragmaDisable
                BangOperators   = $bangCount
                BangNullInit    = $nullInitCount
                BangAssertions  = $bangAssertionCount
                UninitFields    = $uninitFields
            }
        }
    }

    return [PSCustomObject]@{
        TotalFiles           = $totalFiles
        FilesWithEnable      = $filesWithNullableEnable
        NullableDisableCount = $totalNullableDisable
        NullableEnableCount  = $totalNullableEnable
        PragmaDisableCount   = $totalPragmaDisable
        BangOperatorCount    = $totalBangOperator
        BangNullInitCount    = $totalBangNullInit
        BangAssertionCount   = $totalBangAssertions
        UninitFieldCount     = $totalUninitFields
        FilesOfInterest      = $fileDetails
    }
}

#endregion

#region Main

$projectFiles = Get-ProjectFiles -InputPath $Path -Recurse:$Recurse

$results = @()

foreach ($proj in $projectFiles) {
    $projName = [System.IO.Path]::GetFileNameWithoutExtension($proj)

    Write-Verbose "Scanning $projName..."

    $settings = Read-ProjectSettings $proj
    $sourceStats = Scan-SourceFiles $proj

    $results += [PSCustomObject]@{
        Project            = $projName
        Path               = $proj
        Nullable           = $settings.Nullable
        LangVersion        = $settings.LangVersion
        TargetFramework    = $settings.TargetFramework
        WarningEnforcement = $settings.WarningEnforcement
        TotalCsFiles       = $sourceStats.TotalFiles
        FilesWithEnable    = $sourceStats.FilesWithEnable
        NullableDisable    = $sourceStats.NullableDisableCount
        NullableEnable     = $sourceStats.NullableEnableCount
        PragmaDisableCS86  = $sourceStats.PragmaDisableCount
        BangOperators      = $sourceStats.BangOperatorCount
        BangNullInit       = $sourceStats.BangNullInitCount
        BangAssertions     = $sourceStats.BangAssertionCount
        UninitFields       = $sourceStats.UninitFieldCount
        FilesOfInterest    = $sourceStats.FilesOfInterest
    }
}

if ($Json) {
    $results | ConvertTo-Json -Depth 4
    return
}

# Human-readable output
Write-Host ""
Write-Host "=== NRT Readiness Report ===" -ForegroundColor Cyan
Write-Host ""

foreach ($r in $results) {
    Write-Host "Project: $($r.Project)" -ForegroundColor Yellow
    Write-Host "  Path:               $($r.Path)"
    Write-Host "  <Nullable>:         $($r.Nullable)"
    Write-Host "  <LangVersion>:      $($r.LangVersion)"
    Write-Host "  <TargetFramework>:  $($r.TargetFramework)"
    Write-Host "  Warning enforcement: $($r.WarningEnforcement)"
    Write-Host ""
    Write-Host "  Source files:       $($r.TotalCsFiles)"
    Write-Host "  #nullable enable:   $($r.NullableEnable)"
    Write-Host "  #nullable disable:  $($r.NullableDisable)"
    Write-Host "  #pragma CS86xx:     $($r.PragmaDisableCS86)"
    Write-Host "  ! operators (approx): $($r.BangOperators)"
    if ($r.BangOperators -gt 0) {
        Write-Host "    null!/default!:     $($r.BangNullInit)"
        Write-Host "    assertions:         $($r.BangAssertions)"
    }

    if ($r.UninitFields -gt 0 -and $r.Nullable -notmatch "enable") {
        Write-Host "  Uninit ref fields:  ~$($r.UninitFields) (estimated CS8618 warnings)" -ForegroundColor DarkYellow
    }

    if ($r.FilesWithEnable -gt 0 -and $r.Nullable -notmatch "enable") {
        $pct = [math]::Round(($r.FilesWithEnable / $r.TotalCsFiles) * 100, 1)
        Write-Host "  Migration progress: $($r.FilesWithEnable)/$($r.TotalCsFiles) files ($pct%)" -ForegroundColor Green
    }

    # Per-file details — context-dependent heading and content
    $nrtEnabled = $r.Nullable -match "enable"
    $interestFiles = @($r.FilesOfInterest)

    # Filter to files with displayable parts
    $displayFiles = @()
    foreach ($f in $interestFiles) {
        $parts = @()
        if ($f.NullableDisable -gt 0) { $parts += "$($f.NullableDisable) #nullable disable" }
        if ($f.PragmaDisable -gt 0) { $parts += "$($f.PragmaDisable) #pragma" }
        if ($f.BangOperators -gt 5) {
            $bangDetail = "$($f.BangOperators) !"
            if ($f.BangNullInit -gt 0) {
                $bangDetail += " ($($f.BangNullInit) null!/default!, $($f.BangAssertions) assertions)"
            }
            $parts += $bangDetail
        }
        if (-not $nrtEnabled -and $f.UninitFields -gt 5) { $parts += "~$($f.UninitFields) uninit fields" }
        if ($parts.Count -gt 0) {
            $displayFiles += [PSCustomObject]@{ File = $f.File; Detail = ($parts -join ', ') }
        }
    }

    if ($displayFiles.Count -gt 0) {
        Write-Host ""
        if (-not $nrtEnabled) {
            Write-Host "  Migration work needed:" -ForegroundColor Magenta
        } elseif ($r.NullableDisable -gt 0 -or $r.PragmaDisableCS86 -gt 0) {
            Write-Host "  Remaining cleanup:" -ForegroundColor Magenta
        } else {
            Write-Host "  Suppression audit (review ! operators for possible removal):" -ForegroundColor DarkYellow
        }
        foreach ($df in $displayFiles) {
            Write-Host "    $($df.File): $($df.Detail)"
        }
    }

    Write-Host ""
}

# Summary
if (@($results).Count -gt 1) {
    $total = [PSCustomObject]@{
        Projects     = @($results).Count
        CsFiles      = ($results | Measure-Object -Property TotalCsFiles -Sum).Sum
        NullDisable  = ($results | Measure-Object -Property NullableDisable -Sum).Sum
        PragmaCS86   = ($results | Measure-Object -Property PragmaDisableCS86 -Sum).Sum
        BangOps      = ($results | Measure-Object -Property BangOperators -Sum).Sum
        BangNullInit = ($results | Measure-Object -Property BangNullInit -Sum).Sum
        BangAssert   = ($results | Measure-Object -Property BangAssertions -Sum).Sum
        UninitFields = ($results | Measure-Object -Property UninitFields -Sum).Sum
        NrtEnabled   = @($results | Where-Object { $_.Nullable -match "enable" }).Count
    }

    Write-Host "=== Summary ===" -ForegroundColor Cyan
    Write-Host "  Projects scanned:   $($total.Projects)"
    Write-Host "  NRT enabled:        $($total.NrtEnabled)/$($total.Projects)"
    Write-Host "  Total .cs files:    $($total.CsFiles)"
    Write-Host "  Total #nullable disable: $($total.NullDisable)"
    Write-Host "  Total #pragma CS86xx:    $($total.PragmaCS86)"
    Write-Host "  Total ! operators:       $($total.BangOps)"
    if ($total.BangOps -gt 0) {
        Write-Host "    null!/default!:        $($total.BangNullInit)"
        Write-Host "    assertions:            $($total.BangAssert)"
    }
    if ($total.UninitFields -gt 0) {
        Write-Host "  Total uninit ref fields: ~$($total.UninitFields) (estimated CS8618 warnings)"
    }
    Write-Host ""
}

#endregion
