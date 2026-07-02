<#
.SYNOPSIS
    Deterministic OpenTelemetry-coverage detector for scan-agentic-app-perf.

.DESCRIPTION
    Parses the C# / config sources of a .NET agentic app and reports which
    observability primitives are present: Aspire service defaults, the
    OpenTelemetry SDK, an OTLP exporter, tracing/metrics builders, `gen_ai.*`
    token telemetry, and an Aspire dashboard. Automates the mechanical
    presence-detection behind the `otel.*` checks (references/otel-coverage-checks.md)
    so the calling skill spends tokens on judgment (severity, the `why`/`next`
    narrative) rather than on grepping every file.

    This is DETECTION only. It never assigns severity and never writes to the
    scanned project. The skill re-opens each cited file (evidence gate) and fills
    in severity/why/next per the finding schema. If parsing yields nothing, the
    skill falls back to the reference-doc guidance (graceful degradation).

    Works on any topology, including a file-based AppHost (a bare `.cs` with no
    project/solution), because it parses source text directly.

.PARAMETER Path
    App root to scan. Defaults to the current directory.

.PARAMETER Json
    Emit machine-readable JSON (default is a short human summary).

.EXAMPLE
    ./Detect-OtelCoverage.ps1 -Path ./fixture -Json
#>
[CmdletBinding()]
param(
    [string]$Path = ".",
    [switch]$Json
)

$ErrorActionPreference = "Stop"

function Get-ScanFiles {
    param([string]$Root)
    if (Test-Path -LiteralPath $Root -PathType Leaf) { return @((Get-Item -LiteralPath $Root)) }
    Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and
            ($_.Extension -eq '.cs' -or $_.Name -match '^appsettings.*\.json$')
        }
}

# signal-name -> regex. First hit records file:line as evidence.
$signals = [ordered]@{
    serviceDefaults  = 'AddServiceDefaults\s*\('
    addOpenTelemetry = 'AddOpenTelemetry\s*\('
    otlpExporter     = '(?:Add|Use)OtlpExporter\s*\('
    withTracing      = 'WithTracing\s*\('
    withMetrics      = 'WithMetrics\s*\('
    genAiTokens      = 'gen_ai\.usage\.(?:input|output)_tokens'
    aspireDashboard  = 'Aspire\.Hosting\.Dashboard|Dashboard:OtlpEndpointUrl'
    sensitiveData    = 'EnableSensitiveData'
}

$files = @(Get-ScanFiles -Root $Path)
$evidence = [ordered]@{}
foreach ($k in $signals.Keys) { $evidence[$k] = $null }

foreach ($f in $files) {
    $rel = $f.FullName
    try { $rel = (Resolve-Path -LiteralPath $f.FullName -Relative -ErrorAction Stop) } catch {}
    $lines = Get-Content -LiteralPath $f.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($k in $signals.Keys) {
            if ($null -eq $evidence[$k] -and [regex]::IsMatch($line, $signals[$k])) {
                $evidence[$k] = [pscustomobject]@{ file = $rel; line = ($i + 1); text = $line.Trim() }
            }
        }
    }
}

$present = [ordered]@{}
foreach ($k in $signals.Keys) { $present[$k] = ($null -ne $evidence[$k]) }

# Roll up the reference-doc checks. otel.missing-sdk fires only when NEITHER
# Aspire service defaults NOR a raw OTel SDK registration is present.
$hasAnySdk = $present.serviceDefaults -or $present.addOpenTelemetry
$flags = [ordered]@{
    'otel.missing-sdk'        = (-not $hasAnySdk)
    'otel.no-aspire-dashboard' = (-not $present.aspireDashboard)
    'otel.no-token-cost'      = (-not $present.genAiTokens)
}

$result = [ordered]@{
    ok       = $true
    scanned  = @($files).Count
    present  = $present
    evidence = $evidence
    flags    = $flags
    notes    = "Presence detection is best-effort (text match). `gen_ai.*` tags are emitted automatically by Microsoft.Extensions.AI even without an explicit literal, so confirm the exporter wiring before asserting otel.no-token-cost."
}

if ($Json) {
    [pscustomobject]$result | ConvertTo-Json -Depth 6
} else {
    "=== OTel coverage ==="
    "  Files scanned      : $($result.scanned)"
    foreach ($k in $signals.Keys) {
        $mark = if ($present[$k]) { "yes" } else { "no " }
        $loc  = if ($evidence[$k]) { "  ($($evidence[$k].file):$($evidence[$k].line))" } else { "" }
        "  {0,-16} : {1}{2}" -f $k, $mark, $loc
    }
    ""
    foreach ($k in $flags.Keys) { if ($flags[$k]) { "  flag: $k" } }
}
