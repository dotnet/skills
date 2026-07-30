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

    $seen.get_Values() | ForEach-Object {
        # Split the pattern into word keywords, and for an attribute pattern
        # also keep the open bracket form. A skill writes the teaching point
        # closed, `[Category]`, but an eval asserts the real usage, which is
        # always open: `[Category(` or `[Category("positive")]`. Without the
        # open form a file-contains grader checking the attribute never
        # credits the pattern, which pushes eval authors into adding a second
        # grader that makes the agent echo the syntax in its prose. The `[`
        # prefix also marks it as a distinctive code term to the matcher, so
        # a single hit is enough.
        $words = @($_[0].ToLower() -replace '[^a-z0-9._]', ' ' -split '\s+' | Where-Object { $_.Length -gt 1 })
        if ($_[0] -match '^\[(\w+)\]$') { $words += "[$($Matches[1].ToLower())" }

        [PSCustomObject]@{
            Category    = 'CodePattern'
            Description = $_[0]
            Line        = $_[1]
            Keywords    = $words
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

    $found.get_Values()
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

    # Use get_Keys() rather than .Keys: if a significant term is literally
    # "keys", PowerShell's hashtable member access returns that entry's value
    # instead of the key collection, collapsing the keyword set.
    [string[]]$terms.get_Keys()
}

# ═══════════════════════════════════════════════════════════
#  eval.yaml Parsing - extract test evidence
# ═══════════════════════════════════════════════════════════

function Get-LineIndent([string]$line) {
    if ($line -match '^(\s*)\S') { return $Matches[1].Length }
    return -1   # blank or whitespace-only
}

# Strips YAML quoting and applies the escape rules of each quoting style, so
# parsed evidence matches what a real YAML parser would produce: double-quoted
# scalars honour backslash escapes, single-quoted scalars honour '' -> '.
function Remove-YamlQuoting([string]$text) {
    $t = $text.Trim()

    if ($t.Length -ge 2 -and $t.StartsWith('"') -and $t.EndsWith('"')) {
        $inner = $t.Substring(1, $t.Length - 2)
        $sb = [System.Text.StringBuilder]::new()
        for ($k = 0; $k -lt $inner.Length; $k++) {
            if ($inner[$k] -ne '\' -or $k -eq $inner.Length - 1) {
                [void]$sb.Append($inner[$k])
                continue
            }
            $k++
            switch ($inner[$k]) {
                'n'     { [void]$sb.Append("`n") }
                't'     { [void]$sb.Append("`t") }
                'r'     { [void]$sb.Append("`r") }
                '0'     { [void]$sb.Append("`0") }
                default { [void]$sb.Append($inner[$k]) }
            }
        }
        return $sb.ToString()
    }

    if ($t.Length -ge 2 -and $t.StartsWith("'") -and $t.EndsWith("'")) {
        return $t.Substring(1, $t.Length - 2).Replace("''", "'")
    }

    return $t
}

# True when $text either is not a quoted scalar or is one whose closing quote
# has already been seen, honouring \" inside double quotes and '' inside
# single quotes.
function Test-YamlQuotedScalarComplete([string]$text) {
    $t = $text.Trim()
    if ($t.Length -lt 1) { return $true }

    $quote = $t[0]
    if ($quote -ne '"' -and $quote -ne "'") { return $true }
    if ($t.Length -lt 2) { return $false }

    if ($quote -eq '"') {
        for ($k = 1; $k -lt $t.Length; $k++) {
            if ($t[$k] -eq '\') { $k++; continue }
            if ($t[$k] -eq '"') { return $true }
        }
        return $false
    }

    $k = 1
    while ($k -lt $t.Length) {
        if ($t[$k] -eq "'") {
            if ($k + 1 -lt $t.Length -and $t[$k + 1] -eq "'") { $k += 2; continue }
            return $true
        }
        $k++
    }
    return $false
}

# Reads a YAML scalar that may be a block scalar (| or >) or a plain/quoted
# scalar folded across several more-indented continuation lines. Returns the
# joined value plus the index of the last line consumed.
#
# Block scalars, and quoted scalars whose closing quote has not been reached,
# are consumed by indentation alone: everything more indented than the key is
# content, including blank lines, comment-looking lines, `- ` bullets and
# `key:`-shaped lines. Applying the structural breaks used for plain scalars
# would truncate such a value and silently drop evidence.
function Read-YamlScalar {
    param([string[]]$Lines, [int]$Index, [int]$KeyIndent, [string]$Inline)

    $inline = $Inline.Trim()
    $buffer = @()
    $last = $Index
    $isBlock = $false

    if ($inline -match '^[|>][+-]?\d*$') { $inline = ''; $isBlock = $true }
    elseif ($inline) { $buffer += $inline }

    $inQuoted = -not $isBlock -and -not (Test-YamlQuotedScalarComplete ($buffer -join ' '))
    $literal = $isBlock -or $inQuoted

    for ($j = $Index + 1; $j -lt $Lines.Count; $j++) {
        $candidate = $Lines[$j].TrimEnd()
        $indent = Get-LineIndent $candidate
        if ($indent -lt 0) {
            # A blank line ends a plain scalar but is interior content of a
            # block or still-open quoted scalar, which only end on dedent.
            if (-not $literal) { break }
            continue
        }
        if ($indent -le $KeyIndent) { break }

        $trimmed = $candidate.Trim()
        if (-not $literal) {
            if ($trimmed.StartsWith('#')) { break }
            if ($trimmed -match '^-(\s|$)') { break }
            if ($trimmed -match '^[A-Za-z_][\w.-]*:(\s|$)') { break }
        }

        $buffer += $trimmed
        $last = $j

        if ($inQuoted -and (Test-YamlQuotedScalarComplete ($buffer -join ' '))) { break }
    }

    [PSCustomObject]@{
        Value     = ($buffer -join ' ').Trim()
        LastIndex = $last
    }
}

<#
.SYNOPSIS
    Extracts test evidence from an eval.yaml spec.

.DESCRIPTION
    Evidence is anything the eval actually checks: grader configuration
    (regex patterns, literal substrings, expected values, commands) and
    LLM-judged rubric items. Both are matched against SKILL.md teaching
    points by the coverage engine.

    The parser is indentation-driven so it handles the real spec shape:
    graders are a list of `- type:` entries each carrying a nested `config:`
    map, and rubric items are plain (usually unquoted) scalars that commonly
    wrap across several lines.
#>
function Get-EvalEvidence([string]$yamlContent) {
    # Grader config keys whose value is a regular expression.
    $regexKeys = @('pattern', 'stdout_matches')
    # Grader config keys whose value is a literal string.
    $literalKeys = @('substring', 'value', 'stdout_contains', 'command')

    $evidence = @()
    $scenarioCount = 0
    $scenario = '(unknown)'
    $section = 'none'
    $sectionIndent = -1
    $graderType = $null
    # Indent of the `- name:` entries that open a stimulus, learned from the
    # first one seen. Anchoring to it keeps a deeper `- name:` — a rubric
    # bullet, say — from being mistaken for a new scenario and wiping the
    # surrounding evidence.
    $scenarioIndent = -1

    $lines = $yamlContent -split "`n"

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i].TrimEnd()
        $indent = Get-LineIndent $line
        if ($indent -lt 0) { continue }
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('#')) { continue }

        # Leaving the current block ends the section.
        if ($section -ne 'none' -and $indent -le $sectionIndent) {
            $section = 'none'
            $sectionIndent = -1
            $graderType = $null
        }

        # Stimulus boundary: `- name: <scenario>` at the stimulus list indent,
        # outside any graders/rubric block.
        if ($section -eq 'none' -and $indent -ge 2 -and $trimmed -match '^-\s+name:\s*(.+)$' -and
            ($scenarioIndent -lt 0 -or $indent -eq $scenarioIndent)) {
            $scenarioIndent = $indent
            $scenario = Remove-YamlQuoting $Matches[1]
            $scenarioCount++
            $sectionIndent = -1
            $graderType = $null
            continue
        }

        if ($trimmed -eq 'graders:') {
            $section = 'graders'; $sectionIndent = $indent; $graderType = $null; continue
        }
        if ($trimmed -eq 'rubric:') {
            $section = 'rubric'; $sectionIndent = $indent; continue
        }

        if ($section -eq 'graders') {
            if ($trimmed -match '^-\s+type:\s*(.+)$') {
                $graderType = (Remove-YamlQuoting $Matches[1]) -replace '[^\w.-]', ''
                if ($graderType -eq 'exit-success') {
                    $evidence += [PSCustomObject]@{
                        Scenario     = $scenario
                        EvidenceType = 'assertion:exit-success'
                        Content      = 'exit-success: project builds and tests pass'
                        RawPattern   = $null
                    }
                }
                continue
            }

            if ($trimmed -match '^([A-Za-z_][\w.-]*):\s*(.*)$') {
                $key = $Matches[1]
                $rest = $Matches[2]

                $isRegex = $regexKeys -contains $key
                $isLiteral = $literalKeys -contains $key
                # For file-exists style graders the path *is* the assertion.
                $isPath = ($key -eq 'path' -and $graderType -match '^file-(not-)?exists$')

                if (-not ($isRegex -or $isLiteral -or $isPath)) { continue }

                $scalar = Read-YamlScalar -Lines $lines -Index $i -KeyIndent $indent -Inline $rest
                $i = $scalar.LastIndex
                $value = Remove-YamlQuoting $scalar.Value
                if (-not $value) { continue }

                $evidence += [PSCustomObject]@{
                    Scenario     = $scenario
                    EvidenceType = "assertion:$graderType"
                    Content      = $value
                    # Literal values are escaped so the code-pattern matcher
                    # treats them as text rather than as a regex.
                    RawPattern   = if ($isRegex) { $value } else { [regex]::Escape($value) }
                }
            }
            continue
        }

        if ($section -eq 'rubric' -and $trimmed -match '^-\s*(.*)$') {
            $scalar = Read-YamlScalar -Lines $lines -Index $i -KeyIndent $indent -Inline $Matches[1]
            $i = $scalar.LastIndex
            $value = Remove-YamlQuoting $scalar.Value
            if (-not $value) { continue }

            $evidence += [PSCustomObject]@{
                Scenario     = $scenario
                EvidenceType = 'rubric'
                Content      = $value
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
