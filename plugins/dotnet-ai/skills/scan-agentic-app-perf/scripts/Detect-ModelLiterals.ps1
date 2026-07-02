<#
.SYNOPSIS
    Deterministic model-assignment detector for scan-agentic-app-perf.

.DESCRIPTION
    Parses the C# sources of a .NET agentic app and reports where model ids are
    declared: string literals (e.g. `"gpt-4o-mini"`), Aspire/Foundry enum refs
    (e.g. `FoundryModel.OpenAI.Gpt4oMini`), and deployment/registration calls
    (`AddDeployment(...)`, `deploymentName:`, `modelId:`). It separates AppHost
    declarations (the canonical place for model ids) from agent-service `.cs`
    files (where a hard-coded literal trips `model.hardcoded`), and counts the
    distinct model ids to inform `model.same-default`. Automates the mechanical
    extraction behind the `model.*` checks (references/model-assignment-checks.md).

    This is DETECTION only. It never assigns severity and never writes to the
    scanned project. The skill re-opens each cited file (evidence gate) and fills
    in severity/why/next per the finding schema, including the role-vs-model
    judgment (planner/worker) that text matching cannot make. If parsing yields
    nothing, the skill falls back to the reference-doc guidance (graceful
    degradation).

    Works on any topology, including a file-based AppHost (a bare `.cs` with no
    project/solution), because it parses source text directly.

.PARAMETER Path
    App root to scan. Defaults to the current directory.

.PARAMETER Json
    Emit machine-readable JSON (default is a short human summary).

.EXAMPLE
    ./Detect-ModelLiterals.ps1 -Path ./fixture -Json
#>
[CmdletBinding()]
param(
    [string]$Path = ".",
    [switch]$Json
)

$ErrorActionPreference = "Stop"

function Get-SourceFiles {
    param([string]$Root)
    if (Test-Path -LiteralPath $Root -PathType Leaf) { return @((Get-Item -LiteralPath $Root)) }
    Get-ChildItem -LiteralPath $Root -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
}

# Well-known model-id shapes as string literals (kept broad but anchored).
$literalRx = '"(?<id>(?:gpt-|o1|o3|o4|text-embedding-|claude-|gemini-|phi-|llama|mistral|deepseek)[A-Za-z0-9._:-]*)"'
# Aspire/Foundry strongly-typed model refs.
$enumRx    = '(?<id>FoundryModel\.[A-Za-z0-9_.]+)'
# Deployment / registration call sites where a model id is bound.
$deployRx  = '(?:AddDeployment|WithDeployment)\s*\(|deploymentName\s*:|modelId\s*:'

$hits = New-Object System.Collections.Generic.List[object]
$files = @(Get-SourceFiles -Root $Path)

foreach ($f in $files) {
    $rel = $f.FullName
    try { $rel = (Resolve-Path -LiteralPath $f.FullName -Relative -ErrorAction Stop) } catch {}
    $isAppHost = ($f.FullName -match '\.AppHost' ) -or ($f.Directory.Name -match 'AppHost')
    $lines = Get-Content -LiteralPath $f.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($m in [regex]::Matches($line, $literalRx)) {
            $hits.Add([pscustomobject]@{ id = $m.Groups['id'].Value; form = 'literal'; appHost = $isAppHost; file = $rel; line = ($i + 1); text = $line.Trim() })
        }
        foreach ($m in [regex]::Matches($line, $enumRx)) {
            $hits.Add([pscustomobject]@{ id = $m.Groups['id'].Value; form = 'enum'; appHost = $isAppHost; file = $rel; line = ($i + 1); text = $line.Trim() })
        }
        if ([regex]::IsMatch($line, $deployRx)) {
            $hits.Add([pscustomobject]@{ id = $null; form = 'deployment-call'; appHost = $isAppHost; file = $rel; line = ($i + 1); text = $line.Trim() })
        }
    }
}

$hits = [object[]]$hits.ToArray()
$distinctIds = @($hits | Where-Object { $_.id } | Select-Object -ExpandProperty id -Unique)
# Literals living in a non-AppHost source file are what model.hardcoded flags.
$hardcodedInService = @($hits | Where-Object { $_.form -eq 'literal' -and -not $_.appHost })

$flags = [ordered]@{
    # >=2 model ids but only one distinct value hints at model.same-default;
    # the skill confirms agent count and roles before asserting.
    'model.same-default' = ($distinctIds.Count -eq 1 -and @($hits | Where-Object { $_.id }).Count -ge 2)
    'model.hardcoded'    = ($hardcodedInService.Count -gt 0)
}

$result = [ordered]@{
    ok          = $true
    scanned     = @($files).Count
    distinctIds = $distinctIds
    hits        = $hits
    flags       = $flags
    notes       = "Role-vs-model judgment (planner/worker, reasoning-on-deterministic, cheap-on-planner) is NOT decided here; confirm each agent's role from its prompt/tools per the finding schema. AppHost AddDeployment(...) is the canonical model-id location and does not itself trip model.hardcoded."
}

if ($Json) {
    [pscustomobject]$result | ConvertTo-Json -Depth 6
} else {
    "=== Model assignments ==="
    "  Files scanned    : $($result.scanned)"
    "  Distinct model ids: $($distinctIds.Count)  [$($distinctIds -join ', ')]"
    foreach ($h in $hits) {
        $id = if ($h.id) { $h.id } else { "(deployment call)" }
        $scope = if ($h.appHost) { "apphost" } else { "service" }
        "  $($h.form)/$scope : $id  ($($h.file):$($h.line))"
    }
    ""
    foreach ($k in $flags.Keys) { if ($flags[$k]) { "  flag: $k" } }
}
