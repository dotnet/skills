<#
.SYNOPSIS
    Generates per-plugin MSBench runner scripts with embedded plugin content.

.DESCRIPTION
    For each plugin in plugins/, generates a self-contained runner.sh that embeds
    all plugin files (skills, agents, references, scripts, plugin.json) as bash
    heredoc blocks. The generated .sh file recreates the full plugin directory
    structure on the target machine before launching the Copilot CLI agent.

    Uses msbench/agents/plugin-runner.template.sh as the base template.

    Only the final .sh file needs to be copied to the benchmark machine —
    all skill and agent content is embedded within it.

.PARAMETER PluginName
    Generate runner for a specific plugin only. If omitted, generates for all plugins.

.EXAMPLE
    .\Generate-PluginAgents.ps1
    .\Generate-PluginAgents.ps1 -PluginName dotnet-msbuild
#>
[CmdletBinding()]
param(
    [string]$PluginName
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot  = Split-Path -Parent (Split-Path -Parent $ScriptDir)

$PluginsDir   = Join-Path $RepoRoot 'plugins'
$OutputBaseDir = Join-Path $RepoRoot 'msbench' 'agents' 'with-skills'
$TemplatePath  = Join-Path $RepoRoot 'msbench' 'agents' 'plugin-runner.template.sh'

if (-not (Test-Path $PluginsDir)) {
    Write-Error "Plugins directory not found: $PluginsDir"
    return
}

if (-not (Test-Path $TemplatePath)) {
    Write-Error "Template not found: $TemplatePath"
    return
}

# ── Heredoc generation ────────────────────────────────────────────────
# Converts a source file into a bash heredoc block that writes the file
# to $PLUGIN_DIR/<relativePath> at runtime.
function ConvertTo-Heredoc {
    param(
        [string]$SourcePath,
        [string]$RelativePath,    # e.g. skills/csharp-scripts/SKILL.md
        [string]$Delimiter = 'HEREDOC_EOF'
    )

    $content = Get-Content -Raw $SourcePath
    $content = $content.TrimEnd()

    # Replace Unicode box-drawing and special chars with ASCII equivalents
    # so the runner stays cp1252-safe on all platforms
    $content = $content `
        -replace '[┌┐└┘╔╗╚╝]', '+' `
        -replace '[─━═]', '-' `
        -replace '[│┃║]', '|' `
        -replace '[├┤╠╣]', '+' `
        -replace '[┬┴╦╩╬┼]', '+' `
        -replace '[▼▶▷▲◀◁]', '>' `
        -replace '\u2192', '->'

    # Compute parent directory for mkdir -p
    $parentDir = if ($RelativePath -match '/') {
        $RelativePath -replace '/[^/]+$', ''
    } else {
        ''
    }

    $block = @()
    if ($parentDir) {
        $block += 'mkdir -p "$PLUGIN_DIR/' + $parentDir + '"'
    }
    $block += 'cat > "$PLUGIN_DIR/' + $RelativePath + '" << ' + "'" + $Delimiter + "'"
    $block += $content
    $block += $Delimiter
    $block += ''

    return ($block -join "`n")
}

# ── Read template ─────────────────────────────────────────────────────
$template = Get-Content -Raw $TemplatePath

# ── Determine plugins to process ──────────────────────────────────────
if ($PluginName) {
    $pluginPath = Join-Path $PluginsDir $PluginName
    if (-not (Test-Path $pluginPath)) {
        Write-Error "Plugin not found: $pluginPath"
        return
    }
    $plugins = @(Get-Item $pluginPath)
} else {
    $plugins = Get-ChildItem -Path $PluginsDir -Directory | Sort-Object Name
}

# ── Generate per-plugin runners ───────────────────────────────────────
foreach ($plugin in $plugins) {
    $pName = $plugin.Name
    Write-Host "`nGenerating runner for plugin: $pName" -ForegroundColor Cyan

    # Collect all files in the plugin
    $allFiles = Get-ChildItem -Path $plugin.FullName -Recurse -File | Sort-Object FullName
    if ($allFiles.Count -eq 0) {
        Write-Warning "  No files found in $($plugin.FullName), skipping."
        continue
    }

    $heredocBlocks = @()
    $agentCount = 0
    $skillCount = 0
    $otherCount = 0

    foreach ($file in $allFiles) {
        $relativePath = $file.FullName.Substring($plugin.FullName.Length + 1).Replace('\', '/')

        # Choose delimiter based on content type
        if ($relativePath -like 'agents/*') {
            $delimiter = 'AGENT_EOF'
            $agentCount++
        }
        elseif ($relativePath -like 'skills/*') {
            $delimiter = 'SKILL_EOF'
            $skillCount++
        }
        else {
            $delimiter = 'FILE_EOF'
            $otherCount++
        }

        Write-Host "  Embedding: $relativePath" -ForegroundColor DarkCyan
        $heredocBlocks += ConvertTo-Heredoc -SourcePath $file.FullName `
                                            -RelativePath $relativePath `
                                            -Delimiter $delimiter
    }

    $generatedContent = $heredocBlocks -join "`n"

    # Apply template substitutions
    $output = $template -replace '@@PLUGIN_NAME@@', $pName
    $output = $output -replace '# @@GENERATED_PLUGIN_FILES@@', $generatedContent

    # Normalize to LF line endings for bash
    $output = $output -replace "`r`n", "`n"

    # Ensure output directory exists
    $outputDir = Join-Path $OutputBaseDir $pName
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    # Write runner.sh
    $outputPath = Join-Path $outputDir 'runner.sh'
    [System.IO.File]::WriteAllText($outputPath, $output, [System.Text.UTF8Encoding]::new($false))

    # Write config.yaml
    $configYaml = @"
# Agent configuration for Copilot CLI with $pName plugin skills
agent:
  name: "github-copilot-cli"
  description: "GitHub Copilot CLI with $pName plugin skills"
  tags:
    skills: "enabled"
    plugin: "$pName"

skills:
  enabled: true
  plugin: "$pName"

resources:
  timeout_sec: 600
"@
    $configPath = Join-Path $outputDir 'config.yaml'
    $configYaml = $configYaml -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($configPath, $configYaml, [System.Text.UTF8Encoding]::new($false))

    Write-Host "  -> $outputPath ($agentCount agents, $skillCount skill files, $otherCount other)" -ForegroundColor Green
}

Write-Host "`nAll plugin agents generated." -ForegroundColor Green
