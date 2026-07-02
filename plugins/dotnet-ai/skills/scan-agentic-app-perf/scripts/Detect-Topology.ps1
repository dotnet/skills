<#
.SYNOPSIS
    Deterministic topology detector for scan-agentic-app-perf.

.DESCRIPTION
    Parses the C# sources of a .NET agentic app and emits a best-effort agent
    handoff graph: agent nodes, handoff edges, orphaned agents, and per-file
    fan-out. Automates the mechanical extraction behind the `topology.*` checks
    (references/topology-checks.md) so the graph is consistent run-to-run and the
    calling skill spends tokens on judgment (cycle / deep-single-leaf severity,
    the `why`/`next` narrative) rather than on reading every source file.

    This is DETECTION only. It never assigns severity and never writes to the
    scanned project. The skill re-opens each cited file (evidence gate) and fills
    in severity/why/next per the finding schema. If parsing yields nothing, the
    skill falls back to the reference-doc guidance (graceful degradation).

    Works on any topology, including a file-based AppHost (a bare `.cs` with no
    project/solution), because it parses source text directly.

.PARAMETER Path
    App root to scan (directory containing the solution / AppHost / agent
    sources, or a single file-based AppHost `.cs`). Defaults to the current dir.

.PARAMETER Json
    Emit machine-readable JSON (default is a short human summary).

.EXAMPLE
    ./Detect-Topology.ps1 -Path ./fixture -Json
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

# Node-declaration patterns. Each captures the agent's name/alias where present.
$nodePatterns = @(
    # Aspire AppHost project registration: AddProject<Projects.X>("alias")
    @{ kind = 'project'; rx = 'AddProject<[^>]+>\s*\(\s*"(?<name>[^"]+)"' },
    # MAF host agent registration: AddAIAgent("name", ...)
    @{ kind = 'agent';   rx = 'AddAIAgent\s*\(\s*"(?<name>[^"]+)"' },
    # AsAIAgent(name: "name", ...)
    @{ kind = 'agent';   rx = 'As(?:AI)?Agent\s*\(\s*name\s*:\s*"(?<name>[^"]+)"' },
    # CreateAIAgent(name: "name")
    @{ kind = 'agent';   rx = 'CreateAIAgent\s*\(\s*(?:name\s*:\s*)?"(?<name>[^"]+)"' },
    # new ChatClientAgent(... ) — name not always present; recorded per-file
    @{ kind = 'agent';   rx = 'new\s+ChatClientAgent\s*\(' }
)

# Handoff-edge patterns: AddHandoff("target") / WithHandoff("target")
$edgePatterns = @(
    'Add(?:Handoff|Edge)\s*\(\s*"(?<target>[^"]+)"',
    'WithHandoff\s*\(\s*"(?<target>[^"]+)"'
)

$files = @(Get-SourceFiles -Root $Path)
$nodes = New-Object System.Collections.Generic.List[object]
$edges = New-Object System.Collections.Generic.List[object]
$fanoutByFile = @{}

foreach ($f in $files) {
    $rel = $f.FullName
    try { $rel = (Resolve-Path -LiteralPath $f.FullName -Relative -ErrorAction Stop) } catch {}
    $lines = Get-Content -LiteralPath $f.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($np in $nodePatterns) {
            foreach ($m in [regex]::Matches($line, $np.rx)) {
                $name = if ($m.Groups['name'].Success) { $m.Groups['name'].Value } else { [System.IO.Path]::GetFileNameWithoutExtension($f.Name) }
                $nodes.Add([pscustomobject]@{ name = $name; kind = $np.kind; file = $rel; line = ($i + 1) })
            }
        }
        foreach ($ep in $edgePatterns) {
            foreach ($m in [regex]::Matches($line, $ep)) {
                $src = [System.IO.Path]::GetFileNameWithoutExtension($f.Directory.Name) # best-effort: source project/dir
                if ([string]::IsNullOrWhiteSpace($src)) { $src = [System.IO.Path]::GetFileNameWithoutExtension($f.Name) }
                $edges.Add([pscustomobject]@{ source = $src; target = $m.Groups['target'].Value; file = $rel; line = ($i + 1) })
                if (-not $fanoutByFile.ContainsKey($rel)) { $fanoutByFile[$rel] = 0 }
                $fanoutByFile[$rel]++
            }
        }
    }
}

# Materialize the generic Lists as arrays. PowerShell's array-subexpression
# @(...) .Count throws "Argument types do not match" on a List[object] of
# PSCustomObjects, so cast once and treat everything as arrays downstream.
$nodes = [object[]]$nodes.ToArray()
$edges = [object[]]$edges.ToArray()

$nodeNames = @($nodes | Select-Object -ExpandProperty name -Unique)
$edgeTargets = @($edges | Select-Object -ExpandProperty target -Unique)
$edgeSources = @($edges | Select-Object -ExpandProperty source -Unique)
$touched = @($edgeTargets + $edgeSources | Select-Object -Unique)
$orphans = @($nodeNames | Where-Object { $touched -notcontains $_ })
$maxFanout = 0
if ($fanoutByFile.Values.Count -gt 0) { $maxFanout = ($fanoutByFile.Values | Measure-Object -Maximum).Maximum }

$metrics = [ordered]@{
    agentCount  = @($nodeNames).Count
    edgeCount   = @($edges).Count
    orphanCount = @($orphans).Count
    maxFanout   = [int]$maxFanout
}
$result = [ordered]@{
    ok      = $true
    scanned = @($files).Count
    metrics = $metrics
    nodes   = @($nodes)
    edges   = @($edges)
    orphans = @($orphans)
    notes   = "Edge source is inferred from the defining file/project (best-effort); confirm direction against the cited file before asserting a cycle or deep chain."
}

if ($Json) {
    [pscustomobject]$result | ConvertTo-Json -Depth 6
} else {
    "=== Topology ==="
    "  Files scanned : $($result.scanned)"
    "  Agents        : $($metrics.agentCount)  [$($nodeNames -join ', ')]"
    "  Handoff edges : $($metrics.edgeCount)"
    "  Orphan agents : $($metrics.orphanCount)  [$($orphans -join ', ')]"
    "  Max fan-out   : $($metrics.maxFanout) (handoffs in one file)"
    foreach ($e in $edges) { "  edge: $($e.source) -> $($e.target)  ($($e.file):$($e.line))" }
}
