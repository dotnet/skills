# Build entry point for the agentic-workflows knowledge bundles.
# Validates skills and compiles knowledge lock files.
# Run: pwsh agentic-workflows/dotnet-msbuild/build.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$SkillsDir = Join-Path $RepoRoot 'plugins' 'dotnet-msbuild' 'skills'
$DomainGatePattern = 'Only activate in MSBuild/\.NET build context'

# ── Step 1: Validate skills ─────────────────────────────────────────

Write-Host '=== Validating skills ===' -ForegroundColor Cyan
Write-Host ''

$errors = 0

$skillDirs = Get-ChildItem -Path $SkillsDir -Directory |
    Where-Object { $_.Name -ne 'shared' }

foreach ($dir in $skillDirs) {
    $skillFile = Join-Path $dir.FullName 'SKILL.md'
    if (-not (Test-Path $skillFile)) { continue }

    $content = Get-Content $skillFile -Raw

    if ($content -notmatch '(?s)^---\s*\r?\n(.*?)\r?\n---') {
        Write-Host "❌ $($dir.Name): Missing YAML frontmatter" -ForegroundColor Red
        $errors++
        continue
    }

    $frontmatter = $Matches[1]
    if ($frontmatter -notmatch 'description:\s*"([^"]*)"') {
        Write-Host "❌ $($dir.Name): Missing description in frontmatter" -ForegroundColor Red
        $errors++
        continue
    }

    $description = $Matches[1]

    if ($description -notmatch $DomainGatePattern) {
        Write-Host "❌ $($dir.Name): Description missing domain gate. Must include 'Only activate in MSBuild/.NET build context.'" -ForegroundColor Red
        $errors++
    }
}

if ($errors -gt 0) {
    Write-Host "`n$errors validation error(s) found." -ForegroundColor Red
    exit 1
} else {
    Write-Host "✅ All $($skillDirs.Count) skills pass validation.`n" -ForegroundColor Green
}

# ── Step 2: Compile knowledge bundles ────────────────────────────────

Write-Host '=== Compiling knowledge ===' -ForegroundColor Cyan
Write-Host ''

$KnowledgeGroups = [ordered]@{
    'build-errors' = @(
        'binlog-failure-analysis'
        'binlog-generation'
        'check-bin-obj-clash'
        'including-generated-files'
    )
    'performance' = @(
        'build-perf-baseline'
        'build-perf-diagnostics'
        'incremental-build'
        'build-parallelism'
        'eval-performance'
    )
    'style-and-modernization' = @(
        'msbuild-antipatterns'
        'msbuild-modernization'
        'directory-build-organization'
    )
}

$KnowledgeTargets = @{
    'agentic-workflows' = @{
        OutputDir = Join-Path $PSScriptRoot 'shared' 'compiled'
        MaxChars  = 40000
    }
}

function Read-Skill([string]$SkillName) {
    $skillDir = Join-Path $SkillsDir $SkillName
    $skillPath = Join-Path $skillDir 'SKILL.md'
    if (-not (Test-Path $skillPath)) {
        Write-Host "  ⚠ Skill not found: $SkillName ($skillPath)" -ForegroundColor Yellow
        return $null
    }

    $content = Get-Content $skillPath -Raw

    # Strip YAML frontmatter (tolerate both LF and CRLF)
    if ($content -match '(?s)^---\r?\n.*?\r?\n---\r?\n(.*)$') {
        $content = $Matches[1]
    }

    # Inline linked references: replace [text](references/file.md) with file content
    $content = [regex]::Replace($content, '\[([^\]]*)\]\((references/[^\)]+\.md)\)', {
        param($m)
        $refPath = Join-Path $skillDir $m.Groups[2].Value
        if (Test-Path $refPath) {
            $refContent = (Get-Content $refPath -Raw).Trim()
            return $refContent
        }
        return $m.Value
    })

    return $content.Trim()
}

function Compile-KnowledgeFile([string]$OutputName, [string[]]$SkillNames, [string]$OutputDir, [int]$MaxChars) {
    $ext = '.lock.md'
    Write-Host "  Compiling: $OutputName$ext"

    $sections = [System.Collections.Generic.List[string]]::new()
    $totalChars = 0

    $header = "<!-- AUTO-GENERATED — DO NOT EDIT -->`n`n"
    $totalChars += $header.Length

    foreach ($skillName in $SkillNames) {
        $content = Read-Skill $skillName
        if ($null -eq $content) { continue }

        if ($totalChars + $content.Length -gt $MaxChars) {
            Write-Host "    ⚠ Truncating $skillName — would exceed $MaxChars char limit" -ForegroundColor Yellow
            $remaining = $MaxChars - $totalChars
            if ($remaining -gt 500) {
                $sections.Add("## $skillName`n`n$($content.Substring(0, $remaining))`n`n[truncated]")
                $totalChars += $remaining
            }
            break
        }

        $sections.Add($content)
        $totalChars += $content.Length
        Write-Host "    ✓ $skillName ($($content.Length.ToString('N0')) chars)"
    }

    $output = $header + ($sections -join "`n`n---`n`n")
    $outputPath = Join-Path $OutputDir "$OutputName$ext"
    [System.IO.File]::WriteAllText($outputPath, $output)
    Write-Host "    → $OutputName$ext ($($output.Length.ToString('N0')) chars total)"
}

function Compile-Target([string]$TargetName, [hashtable]$Config) {
    Write-Host "`n📦 Target: $TargetName"
    Write-Host "   Output: $($Config.OutputDir)"

    New-Item -Path $Config.OutputDir -ItemType Directory -Force | Out-Null

    foreach ($entry in $KnowledgeGroups.GetEnumerator()) {
        Compile-KnowledgeFile -OutputName $entry.Key -SkillNames $entry.Value -OutputDir $Config.OutputDir -MaxChars $Config.MaxChars
    }
}

Write-Host "Skills source: $SkillsDir"

foreach ($entry in $KnowledgeTargets.GetEnumerator()) {
    Compile-Target -TargetName $entry.Key -Config $entry.Value
}

Write-Host "`n✅ Build complete." -ForegroundColor Green
