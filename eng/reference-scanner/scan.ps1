<#
.SYNOPSIS
    Reference scanner for dotnet/skills.

.DESCRIPTION
    Scans skill and agent markdown files for external references and
    potentially dangerous patterns. Outputs GitHub Actions annotations
    when running in CI, or human-readable text otherwise.

.PARAMETER All
    Scan all skill/agent files in the repository.

.PARAMETER Paths
    Specific files to scan.

.PARAMETER RepoRoot
    Repository root path. Defaults to two levels up from this script.

.EXAMPLE
    pwsh eng/reference-scanner/scan.ps1 -All
    pwsh eng/reference-scanner/scan.ps1 -Paths plugins/dotnet/skills/foo/SKILL.md
#>

[CmdletBinding()]
param(
    [switch]$All,
    [string[]]$Paths,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
}

$KnownDomainsFile = Join-Path $PSScriptRoot 'known-domains.txt'

# ---------------------------------------------------------------------------
# Load known domains
# ---------------------------------------------------------------------------

function Get-KnownDomains {
    if (-not (Test-Path $KnownDomainsFile)) { return @() }
    $domains = @()
    foreach ($line in (Get-Content $KnownDomainsFile -Encoding UTF8)) {
        $line = $line.Trim()
        if ($line -and -not $line.StartsWith('#')) {
            $domains += $line.ToLower()
        }
    }
    return $domains
}

# ---------------------------------------------------------------------------
# Domain matching
# ---------------------------------------------------------------------------

function Test-KnownDomain {
    param([string]$Url, [string[]]$KnownDomains)

    $urlLower = $Url.ToLower()
    foreach ($domain in $KnownDomains) {
        if ($domain.Contains('/')) {
            # Path-scoped: require /, ?, #, or end-of-string after the prefix
            if ($urlLower -match "^https?://(www\.)?$([regex]::Escape($domain))([/?#]|$)") {
                return $true
            }
        }
        else {
            # Extract host from URL
            $host_ = ($urlLower -replace '^https?://', '') -replace '[/:\?#].*$', ''
            if ($host_ -eq $domain -or $host_.EndsWith(".$domain")) {
                return $true
            }
        }
    }
    return $false
}

function Test-LocalUrl {
    param([string]$Url)
    $lower = $Url.ToLower()
    # Match localhost, 127.0.0.1, [::1], and wildcard listen addresses (+, *)
    # followed by :, /, or end-of-string
    return ($lower -match '^https?://localhost([:/]|$)' -or
            $lower -match '^https?://127\.0\.0\.1([:/]|$)' -or
            $lower -match '^https?://\[::1\]([:/]|$)' -or
            $lower -match '^https?://[+*]([:/]|$)')
}

function Test-HttpNotHttps {
    param([string]$Url)
    $lower = $Url.ToLower()
    # Extract host and check against schemas.microsoft.com exactly
    $host_ = ($lower -replace '^https?://', '') -replace '[/:\?#].*$', ''
    return ($lower.StartsWith('http://') -and
            -not (Test-LocalUrl $Url) -and
            $host_ -ne 'schemas.microsoft.com' -and
            -not $host_.EndsWith('.schemas.microsoft.com'))
}

# ---------------------------------------------------------------------------
# File discovery
# ---------------------------------------------------------------------------

function Get-SkillFiles {
    param([string]$Root)

    $files = @()

    # plugins/**/SKILL.md and plugins/**/*.agent.md
    $files += @(Get-ChildItem -Path (Join-Path $Root 'plugins') -Recurse -Filter 'SKILL.md' -ErrorAction SilentlyContinue)
    $files += @(Get-ChildItem -Path (Join-Path $Root 'plugins') -Recurse -Filter '*.agent.md' -ErrorAction SilentlyContinue)

    # plugins/**/references/*.md
    $files += @(Get-ChildItem -Path (Join-Path $Root 'plugins') -Recurse -Directory -Filter 'references' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem -Path $_.FullName -Filter '*.md' -ErrorAction SilentlyContinue })

    # .agents/**/*.md
    $agentsDir = Join-Path $Root '.agents'
    if (Test-Path $agentsDir) {
        $files += @(Get-ChildItem -Path $agentsDir -Recurse -Filter '*.md' -ErrorAction SilentlyContinue)
    }

    # agentic-workflows/**/*.md
    $awDir = Join-Path $Root 'agentic-workflows'
    if (Test-Path $awDir) {
        $files += @(Get-ChildItem -Path $awDir -Recurse -Filter '*.md' -ErrorAction SilentlyContinue)
    }

    # eng/**/*.html
    $files += @(Get-ChildItem -Path (Join-Path $Root 'eng') -Recurse -Filter '*.html' -ErrorAction SilentlyContinue)

    # README.md
    $readme = Join-Path $Root 'README.md'
    if (Test-Path $readme) { $files += @(Get-Item $readme) }

    return ($files | Sort-Object FullName -Unique)
}

# ---------------------------------------------------------------------------
# Scanning
# ---------------------------------------------------------------------------

class RefFinding {
    [string]$Path
    [int]$LineNum
    [string]$Level   # "error"
    [string]$Code
    [string]$Message
}

function Invoke-ScanFile {
    param(
        [string]$FilePath,
        [string]$Root,
        [string[]]$KnownDomains
    )

    $findings = [System.Collections.Generic.List[RefFinding]]::new()
    $relPath = $FilePath
    try {
        $relPath = [System.IO.Path]::GetRelativePath($Root, $FilePath)
    }
    catch {
        if ($FilePath.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relPath = $FilePath.Substring($Root.Length)
        }
    }
    $relPath = $relPath.TrimStart('\', '/') -replace '\\', '/'

    try {
        $lines = @(Get-Content $FilePath -Encoding UTF8)
    }
    catch {
        $errorFinding = [RefFinding]::new()
        $errorFinding.Path = $relPath
        $errorFinding.LineNum = 0
        $errorFinding.Level = 'error'
        $errorFinding.Code = 'FILE-READ-ERROR'
        $errorFinding.Message = "Failed to read file: $($_.Exception.Message)"
        $findings.Add($errorFinding)
        return $findings
    }

    $urlPattern = [regex]'https?://[^\s\)\]\"''<>;]+'
    $pipeToShell = [regex]'curl\s[^|]*\|\s*(ba)?sh\b|wget\s[^|]*\|\s*(ba)?sh\b'
    $sriIntegrity = [regex]'(?i)integrity\s*='
    $externalSrc = [regex]'(?i)src\s*=\s*["\x27]https?://'

    # Multi-line <script> tag detection: join file content to catch attributes spanning lines
    $fullContent = $lines -join "`n"
    $scriptTagMultiLine = [regex]'(?is)<script\s[^>]*src\s*=\s*["\x27][^"\x27]*["\x27][^>]*>'
    $sriProtectedUrls = @()

    foreach ($m in $scriptTagMultiLine.Matches($fullContent)) {
        $tag = $m.Value
        $hasSri = $sriIntegrity.IsMatch($tag)
        # Find line number from character offset
        $tagLineNum = ($fullContent.Substring(0, $m.Index) -split "`n").Count

        if ($externalSrc.IsMatch($tag)) {
            $scriptUrl = $null
            if ($tag -match '(?i)src\s*=\s*["' + "'" + ']([^"' + "'" + ']+)') {
                $scriptUrl = $matches[1]
            }
            if ($hasSri) {
                if ($scriptUrl) { $sriProtectedUrls += $scriptUrl }
            }
            elseif (-not $scriptUrl -or -not (Test-LocalUrl $scriptUrl)) {
                $f = [RefFinding]::new()
                $f.Path = $relPath; $f.LineNum = $tagLineNum; $f.Level = 'error'
                $f.Code = 'SCRIPT-NO-SRI'
                $f.Message = 'External script tag without integrity (SRI) attribute'
                $findings.Add($f)
            }
        }
    }

    # Placeholder host patterns: {template} variables, your-*/your_*/yourusername
    # hosts, and well-known example domains. Applied to the host portion only to
    # avoid bypassing domain checks via path (e.g. https://evil.com/placeholder).
    $placeholderHost = [regex]'(?i)(\{[^}]+\}|your[-_]?\w*name\w*|your[-_]\w+|example\.(com|org|net)|contoso\.com)'

    # Line-by-line scanning for URLs and pipe-to-shell
    $inFencedBlock = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $lineNum = $i + 1

        # Track fenced code blocks per CommonMark: opening fences may have an
        # info string (e.g. ```csharp), closing fences must be only fence chars
        # and optional whitespace. Closing must match char type and >= length.
        if ($line -match '^\s{0,3}(`{3,}|~{3,})') {
            $fenceMatch = $matches[1]
            $fenceChar = $fenceMatch[0]
            $fenceLen = $fenceMatch.Length
            if (-not $inFencedBlock) {
                $inFencedBlock = $true
                $openFenceChar = $fenceChar
                $openFenceLen = $fenceLen
            }
            elseif ($fenceChar -eq $openFenceChar -and $fenceLen -ge $openFenceLen -and
                    $line -match '^\s{0,3}[`~]+\s*$') {
                $inFencedBlock = $false
            }
            continue
        }

        # Pipe-to-shell (except known-safe .NET install scripts)
        if ($pipeToShell.IsMatch($line)) {
            $allowedPipeUrls = @(
                'https://dot.net/v1/dotnet-install.sh',
                'https://aka.ms/dotnet-install.sh'
            )
            $isPipeAllowed = $false
            foreach ($allowed in $allowedPipeUrls) {
                # Require the URL to end at a word boundary (space, quote, pipe, or end-of-string)
                if ($line -match "$([regex]::Escape($allowed))(\s|['""|]|$)") { $isPipeAllowed = $true; break }
            }
            if (-not $isPipeAllowed) {
                $f = [RefFinding]::new()
                $f.Path = $relPath; $f.LineNum = $lineNum; $f.Level = 'error'
                $f.Code = 'PIPE-TO-SHELL'
                $f.Message = 'Pipe-to-shell pattern: content is downloaded and piped directly to a shell interpreter'
                $findings.Add($f)
            }
        }

        # All URLs
        foreach ($m in $urlPattern.Matches($line)) {
            $url = $m.Value.TrimEnd('.', ',', ';', ':', ')', "'", '"')

            # Skip placeholder/template URLs.
            # Extract host to avoid bypassing via path (e.g. evil.com/your-app).
            $urlHost = ($url -replace '^https?://', '') -replace '[/:\?#].*$', ''
            if ($placeholderHost.IsMatch($urlHost)) { continue }

            # Inside fenced code blocks, skip HTTP-not-HTTPS (code examples
            # legitimately use http:// listen addresses) but still check
            # external domains -- agents see raw text, not rendered markdown.
            if ($inFencedBlock) {
                if (-not (Test-KnownDomain $url $KnownDomains) -and -not (Test-LocalUrl $url) -and
                    $url -notin $sriProtectedUrls) {
                    $f = [RefFinding]::new()
                    $f.Path = $relPath; $f.LineNum = $lineNum; $f.Level = 'error'
                    $f.Code = 'EXTERNAL-DOMAIN'
                    $f.Message = "Domain not in known-domains.txt -- add it to eng/reference-scanner/known-domains.txt in your PR if this reference is intentional: $url"
                    $findings.Add($f)
                }
                continue
            }

            if (Test-HttpNotHttps $url) {
                $f = [RefFinding]::new()
                $f.Path = $relPath; $f.LineNum = $lineNum; $f.Level = 'error'
                $f.Code = 'HTTP-NOT-HTTPS'
                $f.Message = "Insecure http:// URL (use https://): $url"
                $findings.Add($f)
            }
            elseif (-not (Test-KnownDomain $url $KnownDomains) -and -not (Test-LocalUrl $url) -and $url -notin $sriProtectedUrls) {
                $f = [RefFinding]::new()
                $f.Path = $relPath; $f.LineNum = $lineNum; $f.Level = 'error'
                $f.Code = 'EXTERNAL-DOMAIN'
                $f.Message = "Domain not in known-domains.txt -- add it to eng/reference-scanner/known-domains.txt in your PR if this reference is intentional: $url"
                $findings.Add($f)
            }
        }
    }

    return $findings
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

$knownDomains = Get-KnownDomains

# Determine files to scan
if ($All) {
    $files = Get-SkillFiles $RepoRoot
}
elseif ($Paths) {
    $files = $Paths | Where-Object { Test-Path $_ } | ForEach-Object { Get-Item $_ }
}
elseif ($env:CHANGED_FILES) {
    $files = $env:CHANGED_FILES -split "`n" |
        Where-Object { $_ -match '\.(md|html)$' -and (Test-Path $_) } |
        ForEach-Object { Get-Item $_ }
}
else {
    $files = Get-SkillFiles $RepoRoot
}

$allFindings = [System.Collections.Generic.List[RefFinding]]::new()
foreach ($file in $files) {
    $results = @(Invoke-ScanFile $file.FullName $RepoRoot $knownDomains)
    foreach ($r in $results) {
        if ($r) { $allFindings.Add($r) }
    }
}

$errorCount = ($allFindings | Measure-Object).Count
$fileCount = ($files | Measure-Object).Count
$isCI = $env:GITHUB_ACTIONS -eq 'true'

if ($isCI) {
    foreach ($f in $allFindings) {
        Write-Host "::error file=$($f.Path),line=$($f.LineNum)::[$($f.Code)] $($f.Message)"
    }
}
else {
    if ($errorCount -gt 0) {
        Write-Host "`n  $errorCount error(s):`n"
        foreach ($f in $allFindings) {
            Write-Host "  $([char]0x274C) $($f.Path):$($f.LineNum) [$($f.Code)] $($f.Message)"
        }
    }
    else {
        Write-Host "`n  No external reference issues found.`n"
    }
}

Write-Host "`n--- Reference scan: $fileCount file(s) scanned, $errorCount error(s) ---"

if ($errorCount -gt 0) { exit 1 } else { exit 0 }
