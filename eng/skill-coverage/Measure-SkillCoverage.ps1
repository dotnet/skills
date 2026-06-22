#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Measures eval.yaml test coverage of SKILL.md teaching points.

.DESCRIPTION
    Extracts testable concepts from a SKILL.md file - validation checklist items,
    common pitfalls, workflow steps, and key code patterns - then cross-references
    them against eval.yaml assertions and rubric criteria to identify coverage gaps.

    This is analogous to code coverage for skill files: it answers "what parts of
    my skill's guidance are actually verified by eval scenarios?"

.PARAMETER PluginName
    Plugin directory name (e.g., "dotnet-test").

.PARAMETER SkillName
    Skill directory name (e.g., "writing-mstest-tests").

.PARAMETER All
    Analyze all skills across all plugins.

.PARAMETER Format
    Output format: Table (colored console, default) or Json (machine-readable).

.PARAMETER MinCoverage
    Minimum coverage percentage. Exit with code 1 if any skill falls below this.
    Useful for CI gates. Default: 0 (no threshold).

.PARAMETER RepoRoot
    Repository root path. Auto-detected from script location by default.

.EXAMPLE
    ./Measure-SkillCoverage.ps1 -PluginName dotnet-test -SkillName writing-mstest-tests
.EXAMPLE
    ./Measure-SkillCoverage.ps1 -All
.EXAMPLE
    ./Measure-SkillCoverage.ps1 -All -Format Json
.EXAMPLE
    ./Measure-SkillCoverage.ps1 -All -MinCoverage 50
#>
[CmdletBinding(DefaultParameterSetName = 'Single')]
param(
    [Parameter(ParameterSetName = 'Single', Position = 0)]
    [string]$PluginName,

    [Parameter(ParameterSetName = 'Single', Position = 1)]
    [string]$SkillName,

    [Parameter(ParameterSetName = 'All', Mandatory)]
    [switch]$All,

    [ValidateSet('Table', 'Json')]
    [string]$Format = 'Table',

    [ValidateRange(0, 100)]
    [int]$MinCoverage = 0,

    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
}

$script:StopWords = @{}
@(
    'the', 'and', 'for', 'with', 'from', 'that', 'this', 'use', 'uses', 'used',
    'instead', 'also', 'not', 'are', 'was', 'been', 'have', 'has', 'does', 'did',
    'they', 'them', 'their', 'its', 'may', 'must', 'should', 'can', 'will', 'just',
    'only', 'into', 'over', 'after', 'before', 'about', 'such', 'very', 'all',
    'both', 'each', 'some', 'more', 'most', 'other', 'than', 'too', 'but',
    'when', 'where', 'how', 'why', 'using', 'make', 'like', 'need', 'give',
    'made', 'work', 'works', 'working', 'done', 'doing'
) | ForEach-Object { $script:StopWords[$_] = $true }

# ═══════════════════════════════════════════════════════════
#  SKILL.md Parsing - extract testable "coverage points"
# ═══════════════════════════════════════════════════════════

function Get-CoveragePoints([string]$content) {
    @(Get-ValidationItems $content) +
    @(Get-PitfallItems $content) +
    @(Get-WorkflowSteps $content) +
    @(Get-CodePatterns $content)
}

function Get-ValidationItems([string]$content) {
    $lineNum = 0
    $inValidation = $false
    foreach ($line in $content -split "`n") {
        $lineNum++
        if ($line -match '^\s*##\s+Validation') { $inValidation = $true; continue }
        if ($inValidation -and $line -match '^\s*##\s') { break }
        if ($inValidation -and $line -match '^\s*-\s*\[[ x]\]\s+(.+)$') {
            $desc = $Matches[1].Trim()
            [PSCustomObject]@{
                Category    = 'Validation'
                Description = $desc
                Line        = $lineNum
                Keywords    = @(Get-SignificantTerms $desc)
            }
        }
    }
}

function Get-PitfallItems([string]$content) {
    $inSection = $false
    $headerSeen = $false
    $separatorSeen = $false
    $lineNum = 0

    foreach ($line in $content -split "`n") {
        $lineNum++
        if ($line -match '^\s*##\s+Common Pitfalls') {
            $inSection = $true; $headerSeen = $false; $separatorSeen = $false; continue
        }
        if ($inSection -and $line -match '^\s*##\s') { break }
        if (-not $inSection) { continue }

        if ($line -match '^\s*\|.+\|.+\|' -and -not $headerSeen) { $headerSeen = $true; continue }
        if ($headerSeen -and -not $separatorSeen -and $line -match '^\s*\|[-:\s|]+\|') { $separatorSeen = $true; continue }

        if ($separatorSeen -and $line -match '^\s*\|(.+)\|\s*$') {
            # Split on unescaped pipes that aren't inside backtick-quoted code
            $inner = $Matches[1]
            $cells = @($inner -split '(?<!`)\|(?!`)')
            if ($cells.Count -ge 2) {
                $pitfall = $cells[0].Trim()
                $solution = $cells[1].Trim()
            } else {
                $pitfall = $inner.Trim()
                $solution = ''
            }
            if ($pitfall -and $pitfall -notmatch '^[-:\s]+$') {
                $cleanPitfall = $pitfall -replace '`', ''
                $combined = "$pitfall $solution" -replace '`', ''
                [PSCustomObject]@{
                    Category    = 'Pitfall'
                    Description = $cleanPitfall
                    Line        = $lineNum
                    Keywords    = @(Get-SignificantTerms $combined)
                }
            }
        }
    }
}

function Get-WorkflowSteps([string]$content) {
    $lineNum = 0
    $steps = @()
    $current = $null
    $contentBuf = ''
    $inCodeBlock = $false

    foreach ($line in $content -split "`n") {
        $lineNum++
        if ($line -match '^###\s+Step\s+\d+[:.]\s*(.+)') {
            if ($current) {
                $current.Keywords = @(Get-SignificantTerms "$($current.Description) $contentBuf")
                $steps += $current
            }
            $current = [PSCustomObject]@{
                Category    = 'WorkflowStep'
                Description = ($line -replace '^#+\s*', '').Trim()
                Line        = $lineNum
                Keywords    = @()
            }
            $contentBuf = ''
        }
        elseif ($current -and $line -match '^##\s') {
            $current.Keywords = @(Get-SignificantTerms "$($current.Description) $contentBuf")
            $steps += $current
            $current = $null; $contentBuf = ''
        }
        elseif ($current -and $line -match '^```') {
            # Skip code fence markers; toggle tracking to skip block content
            $inCodeBlock = -not $inCodeBlock
        }
        elseif ($current -and -not $inCodeBlock) {
            $contentBuf += " $line"
        }
    }
    if ($current) {
        $current.Keywords = @(Get-SignificantTerms "$($current.Description) $contentBuf")
        $steps += $current
    }
    $steps
}

function Get-CodePatterns([string]$content) {
    $seen = @{}       # key = lowered pattern, value = [pattern, firstLine]
    $inCode = $false
    $block = ''
    $lineNum = 0
    $blockStartLine = 0

    foreach ($line in $content -split "`n") {
        $lineNum++
        if ($line -match '^```') {
            if ($inCode) {
                foreach ($p in @(Get-BlockPatterns $block)) {
                    $key = $p.ToLower()
                    if (-not $seen.ContainsKey($key)) {
                        $seen[$key] = @($p, $blockStartLine)
                    }
                }
                $inCode = $false; $block = ''
            }
            else { $inCode = $true; $blockStartLine = $lineNum }
            continue
        }
        if ($inCode) { $block += "$line`n" }
    }

    $seen.Values | ForEach-Object {
        [PSCustomObject]@{
            Category    = 'CodePattern'
            Description = $_[0]
            Line        = $_[1]
            Keywords    = @($_[0].ToLower() -replace '[^a-z0-9._]', ' ' -split '\s+' | Where-Object { $_.Length -gt 1 })
        }
    }
}

function Get-BlockPatterns([string]$code) {
    $found = @{}

    # .NET attributes: [TestClass], [DataRow], [Timeout(5000)]
    foreach ($m in [regex]::Matches($code, '\[(\w{3,})(?:\([^)]*\))?\]')) {
        $attr = $m.Groups[1].Value
        if ($attr -notin @('assembly', 'get', 'set', 'global', 'return', 'param', 'string', 'int', 'bool')) {
            $found["[$attr]".ToLower()] = "[$attr]"
        }
    }

    # Assert.* method calls
    foreach ($m in [regex]::Matches($code, '\b(Assert\.\w+)')) {
        $v = $m.Groups[1].Value
        $found[$v.ToLower()] = $v
    }

    # Significant code keywords
    @{
        'sealed'           = '\bsealed\s+(class|record)'
        'readonly'         = '\breadonly\b'
        'CancellationToken'= 'CancellationToken'
        'ValueTuple'       = 'IEnumerable<\(|<\(\w'
        'MSTest.Sdk'       = 'MSTest\.Sdk'
        'TestDataRow'      = 'TestDataRow'
        'Parallelize'      = '\bParallelize\b'
        'DoNotParallelize' = '\bDoNotParallelize\b'
    }.GetEnumerator() | ForEach-Object {
        if ($code -match $_.Value) { $found[$_.Key.ToLower()] = $_.Key }
    }

    $found.Values
}

function Get-SignificantTerms([string]$text) {
    $terms = @{}

    # Backtick-quoted code terms
    foreach ($m in [regex]::Matches($text, '`([^`]+)`')) {
        $t = $m.Groups[1].Value.Trim()
        if ($t.Length -gt 1) { $terms[$t.ToLower()] = $true }
    }

    # Code-like terms
    foreach ($m in [regex]::Matches($text, 'Assert\.\w+|\[\w+\]|CancellationToken|ValueTuple|DataRow|DynamicData|TestContext|TestDataRow|MSTest\.Sdk|sealed|readonly|async\s+void')) {
        $terms[$m.Value.ToLower()] = $true
    }

    # Significant English words
    $words = ($text -replace '`[^`]*`', ' ' -replace '[^a-zA-Z0-9_ ]', ' ') -split '\s+'
    foreach ($w in $words) {
        $lower = $w.ToLower()
        if ($lower.Length -gt 3 -and -not $script:StopWords.ContainsKey($lower)) {
            $terms[$lower] = $true
        }
    }

    [string[]]$terms.Keys
}

# ═══════════════════════════════════════════════════════════
#  eval.yaml Parsing - extract test evidence
# ═══════════════════════════════════════════════════════════

function Get-EvalEvidence([string]$yamlContent) {
    $evidence = @()
    $scenarioCount = 0
    $scenario = '(unknown)'
    $section = 'none'
    $assertType = $null

    foreach ($rawLine in $yamlContent -split "`n") {
        $line = $rawLine.TrimEnd()

        # Scenario name
        if ($line -match '^\s+-?\s*name:\s*[''"]?(.+?)[''"]?\s*$') {
            $scenario = $Matches[1]
            $scenarioCount++
            $section = 'none'
            $assertType = $null
        }

        # Section transitions
        if ($line -match '^\s{2,6}assertions:\s*$')  { $section = 'assertions'; continue }
        if ($line -match '^\s{2,6}rubric:\s*$')       { $section = 'rubric'; continue }
        if ($line -match '^\s{2,6}(prompt|setup|timeout|reject_tools|expect_tools|expect_activation):' -and $section -ne 'none') {
            $section = 'none'
            continue
        }

        # Assertion fields
        if ($section -eq 'assertions') {
            if ($line -match '^\s+-\s*type:\s*[''"]?(\S+?)[''"]?\s*$') {
                $assertType = $Matches[1]
                if ($assertType -eq 'exit_success') {
                    $evidence += [PSCustomObject]@{
                        Scenario     = $scenario
                        EvidenceType = 'assertion:exit_success'
                        Content      = 'exit_success: project builds and tests pass'
                        RawPattern   = $null
                    }
                }
            }
            if ($line -match '^\s+pattern:\s*"(.+?)"\s*$') {
                $regex = $Matches[1] -replace '\\\\', '\'
                $evidence += [PSCustomObject]@{
                    Scenario     = $scenario
                    EvidenceType = "assertion:$assertType"
                    Content      = $regex
                    RawPattern   = $regex
                }
            }
            elseif ($line -match "^\s+pattern:\s*'(.+?)'\s*$") {
                $regex = $Matches[1]
                $evidence += [PSCustomObject]@{
                    Scenario     = $scenario
                    EvidenceType = "assertion:$assertType"
                    Content      = $regex
                    RawPattern   = $regex
                }
            }
            # Unquoted pattern (bare YAML scalar)
            elseif ($line -match '^\s+pattern:\s*([^''"\s].+?)\s*$') {
                $regex = $Matches[1]
                $evidence += [PSCustomObject]@{
                    Scenario     = $scenario
                    EvidenceType = "assertion:$assertType"
                    Content      = $regex
                    RawPattern   = $regex
                }
            }
            if ($line -match '^\s+value:\s*[''"](.+?)[''"]') {
                $evidence += [PSCustomObject]@{
                    Scenario     = $scenario
                    EvidenceType = "assertion:$assertType"
                    Content      = $Matches[1]
                    RawPattern   = $null
                }
            }
        }

        # Rubric items
        if ($section -eq 'rubric' -and $line -match '^\s+-\s*[''"](.+?)[''"]') {
            $evidence += [PSCustomObject]@{
                Scenario     = $scenario
                EvidenceType = 'rubric'
                Content      = $Matches[1]
                RawPattern   = $null
            }
        }
    }

    [PSCustomObject]@{
        ScenarioCount = $scenarioCount
        Items         = $evidence
    }
}

# ═══════════════════════════════════════════════════════════
#  Matching Engine - cross-reference points to evidence
# ═══════════════════════════════════════════════════════════

function Find-CoverageMatches($coveragePoints, $evidenceItems) {
    foreach ($cp in $coveragePoints) {
        $matchList = @()

        foreach ($ev in $evidenceItems) {
            $matched = $false

            # Strategy 1: For CodePattern items, test if the literal code pattern
            # would be matched by an assertion regex.
            if ($cp.Category -eq 'CodePattern' -and $ev.RawPattern) {
                try {
                    if ([regex]::IsMatch($cp.Description, $ev.RawPattern, 'IgnoreCase', [TimeSpan]::FromSeconds(1))) {
                        $matched = $true
                    }
                }
                catch { }
            }

            # Strategy 2: Keyword overlap for all item types.
            # Requires 2+ keyword hits, or 1 if it's a distinctive code term.
            if (-not $matched -and $cp.Keywords.Count -gt 0) {
                $evidenceText = $ev.Content.ToLower()
                $hitCount = 0
                $codeTermHit = $false
                foreach ($kw in $cp.Keywords) {
                    $escaped = [regex]::Escape($kw)
                    try {
                        if ($evidenceText -match $escaped) {
                            $hitCount++
                            if ($kw -match '\.' -or $kw -match '^\[' -or
                                $kw -match '^assert' -or $kw -match 'sealed|readonly|async|cancel|valuetuple|datarow|dynamicdata|testcontext') {
                                $codeTermHit = $true
                            }
                        }
                    }
                    catch { }
                }
                if ($hitCount -ge 2 -or ($hitCount -ge 1 -and $codeTermHit)) {
                    $matched = $true
                }
            }

            if ($matched) { $matchList += $ev }
        }

        [PSCustomObject]@{
            CoveragePoint = $cp
            Evidence      = $matchList
            Covered       = $matchList.Count -gt 0
        }
    }
}

# ═══════════════════════════════════════════════════════════
#  Report Formatting
# ═══════════════════════════════════════════════════════════

function Format-TableReport($results, $skillName, $pluginName, $scenarioCount, $evidenceCount) {
    $categoryOrder = @('Validation', 'Pitfall', 'WorkflowStep', 'CodePattern')
    $categoryLabels = @{
        Validation   = 'VALIDATION CHECKLIST'
        Pitfall      = 'COMMON PITFALLS'
        WorkflowStep = 'WORKFLOW STEPS'
        CodePattern  = 'CODE PATTERNS'
    }

    $totalPoints = @($results).Count
    $coveredPoints = @($results | Where-Object Covered).Count
    $overallPct = if ($totalPoints -gt 0) { [math]::Round(100 * $coveredPoints / $totalPoints) } else { 0 }

    Write-Host ''
    Write-Host '  Skill Coverage: ' -NoNewline
    Write-Host "$pluginName/$skillName" -ForegroundColor Cyan
    Write-Host "  Eval scenarios: $scenarioCount | Evidence items: $evidenceCount"
    Write-Host ('  ' + ('-' * 60))

    foreach ($cat in $categoryOrder) {
        $items = @($results | Where-Object { $_.CoveragePoint.Category -eq $cat })
        if ($items.Count -eq 0) { continue }

        $catCovered = @($items | Where-Object Covered).Count
        $catTotal = $items.Count
        $catPct = if ($catTotal -gt 0) { [math]::Round(100 * $catCovered / $catTotal) } else { 0 }

        Write-Host ''
        Write-Host "  $($categoryLabels[$cat])" -ForegroundColor Yellow

        foreach ($item in $items) {
            $desc = $item.CoveragePoint.Description
            if ($desc.Length -gt 55) { $desc = $desc.Substring(0, 52) + '...' }

            if ($item.Covered) {
                $ev = $item.Evidence[0]
                $hasAssertion = ($item.Evidence | Where-Object { $_.EvidenceType -ne 'rubric' } | Select-Object -First 1) -ne $null
                if ($hasAssertion) {
                    # Prefer showing an assertion-backed evidence item
                    $ev = $item.Evidence | Where-Object { $_.EvidenceType -ne 'rubric' } | Select-Object -First 1
                }
                $evLabel = if ($ev.EvidenceType -eq 'rubric') { 'rubric*' }
                           else { $ev.EvidenceType -replace 'assertion:', '' }
                Write-Host '    ' -NoNewline
                Write-Host 'V' -ForegroundColor Green -NoNewline
                Write-Host " $desc " -NoNewline
                Write-Host "[$evLabel]" -ForegroundColor DarkGray
            }
            else {
                Write-Host '    ' -NoNewline
                Write-Host 'X' -ForegroundColor Red -NoNewline
                Write-Host " $desc " -NoNewline
                Write-Host 'NOT COVERED' -ForegroundColor DarkRed
            }
        }

        $pctColor = if ($catPct -ge 80) { 'Green' } elseif ($catPct -ge 50) { 'Yellow' } else { 'Red' }
        Write-Host "    Coverage: $catCovered/$catTotal ($catPct%)" -ForegroundColor $pctColor
    }

    Write-Host ''
    Write-Host ('  ' + ('-' * 60))
    $color = if ($overallPct -ge 80) { 'Green' } elseif ($overallPct -ge 50) { 'Yellow' } else { 'Red' }
    Write-Host "  OVERALL: $coveredPoints/$totalPoints coverage points ($overallPct%)" -ForegroundColor $color

    $uncovered = @($results | Where-Object { -not $_.Covered -and $_.CoveragePoint.Category -ne 'CodePattern' })
    if ($uncovered.Count -gt 0) {
        Write-Host ''
        Write-Host '  Uncovered teaching points (consider adding eval scenarios):' -ForegroundColor Magenta
        foreach ($item in $uncovered) {
            $catTag = switch ($item.CoveragePoint.Category) {
                'Validation'   { 'validation' }
                'Pitfall'      { 'pitfall' }
                'WorkflowStep' { 'step' }
            }
            Write-Host "    - [$catTag] $($item.CoveragePoint.Description)" -ForegroundColor DarkGray
        }
    }
    # Show rubric-only footnote if any items are covered only by rubric
    $rubricOnly = @($results | Where-Object {
        $_.Covered -and -not ($_.Evidence | Where-Object { $_.EvidenceType -ne 'rubric' })
    })
    if ($rubricOnly.Count -gt 0) {
        Write-Host '  * rubric-only: covered by LLM-judged criteria, no deterministic assertion' -ForegroundColor DarkGray
    }
    Write-Host ''

    return $overallPct
}

function Format-JsonReport($results, $skillName, $pluginName, $scenarioCount, $evidenceCount) {
    $totalPoints = @($results).Count
    $coveredPoints = @($results | Where-Object Covered).Count
    $rubricOnlyCount = @($results | Where-Object {
        $_.Covered -and -not ($_.Evidence | Where-Object { $_.EvidenceType -ne 'rubric' })
    }).Count

    $report = [ordered]@{
        skill      = $skillName
        plugin     = $pluginName
        scenarios  = $scenarioCount
        evidence   = $evidenceCount
        summary    = [ordered]@{
            totalPoints      = $totalPoints
            coveredPoints    = $coveredPoints
            rubricOnlyPoints = $rubricOnlyCount
            percentage       = if ($totalPoints -gt 0) { [math]::Round(100.0 * $coveredPoints / $totalPoints, 1) } else { 0 }
        }
        categories = [ordered]@{}
        uncovered  = @()
    }

    foreach ($cat in @('Validation', 'Pitfall', 'WorkflowStep', 'CodePattern')) {
        $items = @($results | Where-Object { $_.CoveragePoint.Category -eq $cat })
        if ($items.Count -eq 0) { continue }
        $catCovered = @($items | Where-Object Covered).Count
        $report.categories[$cat] = [ordered]@{
            total      = $items.Count
            covered    = $catCovered
            percentage = if ($items.Count -gt 0) { [math]::Round(100.0 * $catCovered / $items.Count, 1) } else { 0 }
            items      = @($items | ForEach-Object {
                [ordered]@{
                    description = $_.CoveragePoint.Description
                    line        = $_.CoveragePoint.Line
                    covered     = $_.Covered
                    evidence    = @($_.Evidence | Select-Object -First 3 | ForEach-Object {
                        [ordered]@{ type = $_.EvidenceType; scenario = $_.Scenario }
                    })
                }
            })
        }
    }

    $report.uncovered = @(
        $results | Where-Object { -not $_.Covered } | ForEach-Object {
            [ordered]@{
                category    = $_.CoveragePoint.Category
                description = $_.CoveragePoint.Description
                line        = $_.CoveragePoint.Line
            }
        }
    )

    # Return the report object; caller is responsible for JSON serialization
    $report
}

# ═══════════════════════════════════════════════════════════
#  Discovery & Main
# ═══════════════════════════════════════════════════════════

function Get-SkillEvalPairs([string]$repoRoot, [string]$pluginFilter, [string]$skillFilter) {
    $pairs = @()
    $pluginsDir = Join-Path $repoRoot 'plugins'
    $testsDir = Join-Path $repoRoot 'tests'

    foreach ($pluginDir in Get-ChildItem $pluginsDir -Directory) {
        if ($pluginFilter -and $pluginDir.Name -ne $pluginFilter) { continue }

        $skillsDir = Join-Path $pluginDir.FullName 'skills'
        if (-not (Test-Path $skillsDir)) { continue }

        foreach ($skillDir in Get-ChildItem $skillsDir -Directory) {
            if ($skillFilter -and $skillDir.Name -ne $skillFilter) { continue }

            $skillMd = Join-Path $skillDir.FullName 'SKILL.md'
            if (-not (Test-Path $skillMd)) { continue }

            $evalYaml = Join-Path $testsDir $pluginDir.Name $skillDir.Name 'eval.yaml'

            $pairs += [PSCustomObject]@{
                PluginName = $pluginDir.Name
                SkillName  = $skillDir.Name
                SkillPath  = $skillMd
                EvalPath   = if (Test-Path $evalYaml) { $evalYaml } else { $null }
            }
        }
    }
    $pairs
}

# -- Entry point --

$pairs = @(Get-SkillEvalPairs $RepoRoot $PluginName $SkillName)

if ($pairs.Count -eq 0) {
    Write-Error "No skills found matching plugin='$PluginName' skill='$SkillName' under $RepoRoot"
    return
}

# Warn if running without filters or -All (acts as -All but no aggregate)
if (-not $All -and -not $PluginName -and -not $SkillName -and $pairs.Count -gt 1) {
    Write-Warning "No -PluginName or -SkillName specified; analyzing all $($pairs.Count) skills. Use -All for aggregate summary."
}

$belowThreshold = $false
$allResults = @()
$jsonReports = @()

foreach ($pair in $pairs) {
    $skillContent = Get-Content -Raw $pair.SkillPath
    $coveragePoints = @(Get-CoveragePoints $skillContent)

    if ($coveragePoints.Count -eq 0) {
        if ($Format -eq 'Table') {
            Write-Host ''
            Write-Host "  $($pair.PluginName)/$($pair.SkillName): no extractable coverage points" -ForegroundColor DarkYellow
        }
        continue
    }

    if (-not $pair.EvalPath) {
        $noEvalResults = @($coveragePoints | ForEach-Object {
            [PSCustomObject]@{ CoveragePoint = $_; Evidence = @(); Covered = $false }
        })
        if ($Format -eq 'Json') {
            $jsonReports += Format-JsonReport $noEvalResults $pair.SkillName $pair.PluginName 0 0
        }
        else {
            Write-Host ''
            Write-Host "  $($pair.PluginName)/$($pair.SkillName)" -ForegroundColor DarkYellow -NoNewline
            Write-Host ': no eval.yaml found' -ForegroundColor DarkYellow
            Format-TableReport $noEvalResults $pair.SkillName $pair.PluginName 0 0 | Out-Null
        }
        if ($MinCoverage -gt 0) { $belowThreshold = $true }
        continue
    }

    $evalContent = Get-Content -Raw $pair.EvalPath
    $evalData = Get-EvalEvidence $evalContent
    $results = @(Find-CoverageMatches $coveragePoints $evalData.Items)

    if ($Format -eq 'Json') {
        $jsonReports += Format-JsonReport $results $pair.SkillName $pair.PluginName $evalData.ScenarioCount @($evalData.Items).Count
    }
    else {
        $pct = Format-TableReport $results $pair.SkillName $pair.PluginName $evalData.ScenarioCount @($evalData.Items).Count
        if ($MinCoverage -gt 0 -and $pct -lt $MinCoverage) { $belowThreshold = $true }
    }

    $allResults += $results
}

# Emit JSON output: single object for one skill, array for multiple
if ($Format -eq 'Json') {
    if ($jsonReports.Count -eq 1) {
        $jsonReports[0] | ConvertTo-Json -Depth 8
    }
    else {
        $jsonReports | ConvertTo-Json -Depth 8
    }
}

# Aggregate summary when analyzing multiple skills
if ($All -and $allResults.Count -gt 0 -and $Format -eq 'Table') {
    $totalAll = $allResults.Count
    $coveredAll = @($allResults | Where-Object Covered).Count
    $pct = [math]::Round(100 * $coveredAll / $totalAll)

    Write-Host ('=' * 64)
    Write-Host "  AGGREGATE: $coveredAll/$totalAll coverage points ($pct%) across $($pairs.Count) skills" -ForegroundColor Cyan
    Write-Host ''
}

if ($belowThreshold) {
    Write-Error "One or more skills fell below the minimum coverage threshold of $MinCoverage%."
    exit 1
}
