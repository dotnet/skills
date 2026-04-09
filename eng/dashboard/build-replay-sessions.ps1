<#
.SYNOPSIS
    Flattens native SDK session JSONL files from skill-validator evaluation output
    into a structured directory and generates an AGENTVIZ-compatible manifest.json.

.DESCRIPTION
    Reads sessions.db (SQLite) from each evaluation artifact to discover session
    metadata (skill, scenario, role, run index), locates the corresponding
    events.jsonl files, copies them with meaningful names, and generates a
    manifest.json suitable for AGENTVIZ static manifest mode.

.PARAMETER ResultsDir
    Path to downloaded artifacts (contains skill-validator-results-* directories).

.PARAMETER OutputDir
    Output directory for flattened sessions and manifest.

.PARAMETER Source
    "scheduled" or "pr" -- determines directory structure and tags.

.PARAMETER PrNumber
    PR number (when Source=pr). Ignored for scheduled runs.
#>
param(
    [Parameter(Mandatory)][string]$ResultsDir,
    [Parameter(Mandatory)][string]$OutputDir,
    [string]$Source = "scheduled",
    [int]$PrNumber = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# SQLite query helper: tries sqlite3 CLI first, falls back to Microsoft.Data.Sqlite
function Invoke-SqliteQuery {
    param(
        [string]$DatabasePath,
        [string]$Query
    )

    $sqlite3 = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($sqlite3) {
        $output = & sqlite3 -separator '|' $DatabasePath $Query 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "sqlite3 query failed: $output"
        }
        return @($output | Where-Object { $_ -and $_ -isnot [System.Management.Automation.ErrorRecord] })
    }

    # Fallback: use Microsoft.Data.Sqlite via .NET
    # Try to find the assembly near the skill-validator binary
    $sqliteDll = $null
    $searchPaths = @(
        (Join-Path $PSScriptRoot "../../artifacts/bin/SkillValidator/debug/Microsoft.Data.Sqlite.dll"),
        (Join-Path $PSScriptRoot "../../artifacts/publish/SkillValidator/release/Microsoft.Data.Sqlite.dll")
    )
    foreach ($p in $searchPaths) {
        $resolved = Resolve-Path $p -ErrorAction SilentlyContinue
        if ($resolved) { $sqliteDll = $resolved.Path; break }
    }

    if (-not $sqliteDll) {
        throw "Neither sqlite3 CLI nor Microsoft.Data.Sqlite.dll found. Install sqlite3 or build the skill-validator first."
    }

    $baseDir = Split-Path $sqliteDll

    # Load all required assemblies
    $requiredDlls = @(
        "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.batteries_v2.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        "Microsoft.Data.Sqlite.dll"
    )
    foreach ($dll in $requiredDlls) {
        $dllPath = Join-Path $baseDir $dll
        if (Test-Path $dllPath) { [void][System.Reflection.Assembly]::LoadFrom($dllPath) }
    }

    # Set the native library search path so SQLitePCLRaw can find e_sqlite3
    $rid = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        if ([System.IntPtr]::Size -eq 8) { "win-x64" } else { "win-x86" }
    } elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        "linux-x64"
    } else {
        "osx-x64"
    }
    $nativeDir = Join-Path $baseDir "runtimes/$rid/native"
    if (Test-Path $nativeDir) {
        # Add native dir to PATH so the DllImport resolver finds e_sqlite3
        $pathSeparator = [IO.Path]::PathSeparator
        $env:PATH = "$nativeDir$pathSeparator$env:PATH"
    }

    # Initialize SQLitePCL batteries
    try { [SQLitePCL.Batteries_V2]::Init() } catch { }

    $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection "Data Source=$DatabasePath;Mode=ReadOnly"
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Query
        $reader = $cmd.ExecuteReader()
        $results = @()
        while ($reader.Read()) {
            $values = @()
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                $values += $reader.GetValue($i).ToString()
            }
            $results += ($values -join '|')
        }
        return $results
    }
    finally {
        $conn.Close()
        $conn.Dispose()
    }
}

# Role mapping: code role -> tag
$roleMap = @{
    'baseline'             = 'baseline'
    'with-skill-isolated'  = 'isolated'
    'with-agent-isolated'  = 'isolated'
    'with-skill-plugin'    = 'plugin'
    'with-agent-plugin'    = 'plugin'
}

# Determine date and subdirectory (use UTC for consistency with purge retention)
$dateTag = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
if ($Source -eq 'pr') {
    $subDir = "pr/$PrNumber"
} else {
    $subDir = "scheduled/$dateTag"
}

$sessionsOutDir = Join-Path $OutputDir "sessions/$subDir"
New-Item -ItemType Directory -Path $sessionsOutDir -Force | Out-Null

$manifestSessions = @()

# Find all artifact directories
$artifactDirs = Get-ChildItem -Path $ResultsDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'skill-validator-results-*' }

if (-not $artifactDirs) {
    Write-Warning "No skill-validator-results-* directories found in $ResultsDir"
    # Write empty manifest
    $manifest = @{ generated = (Get-Date -Format 'o'); sessions = @() }
    $manifestPath = Join-Path $OutputDir "manifest.json"
    $manifest | ConvertTo-Json -Depth 10 | Out-File -FilePath $manifestPath -Encoding utf8
    Write-Host "Wrote empty manifest to $manifestPath"
    exit 0
}

foreach ($artifactDir in $artifactDirs) {
    # Extract plugin name from artifact directory name (skill-validator-results-<plugin> or skill-validator-results-<plugin>--<skill>)
    $entryName = $artifactDir.Name -replace '^skill-validator-results-', ''
    $pluginName = ($entryName -split '--')[0]

    # Find timestamped result directory
    $runDir = Get-ChildItem -Path $artifactDir.FullName -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d{8}-\d{6}$' } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if (-not $runDir) {
        Write-Warning "No timestamped run directory found in $($artifactDir.Name), skipping"
        continue
    }

    $sessionsDbPath = Join-Path $runDir.FullName "sessions.db"
    if (-not (Test-Path $sessionsDbPath)) {
        Write-Warning "No sessions.db found in $($runDir.FullName), skipping"
        continue
    }

    Write-Host "Processing sessions from $($artifactDir.Name) ($($runDir.Name))..."

    # Query sessions.db
    $query = "SELECT id, skill_name, scenario_name, role, run_index, model, status, config_dir FROM sessions WHERE status IN ('completed', 'timed_out') ORDER BY skill_name, scenario_name, run_index, role;"
    try {
        $rows = Invoke-SqliteQuery -DatabasePath $sessionsDbPath -Query $query
    }
    catch {
        Write-Warning "Failed to query $sessionsDbPath : $_"
        continue
    }

    foreach ($row in $rows) {
        if (-not $row) { continue }
        $fields = $row -split '\|'
        if ($fields.Count -lt 8) { continue }

        $sessionId    = $fields[0]
        $skillName    = $fields[1]
        $scenarioName = $fields[2]
        $role         = $fields[3]
        $runIndex     = $fields[4]
        $model        = $fields[5]
        $status       = $fields[6]
        $configDir    = $fields[7]

        # Map role to tag
        $roleTag = if ($roleMap.ContainsKey($role)) { $roleMap[$role] } else { $role }

        # Find events.jsonl: sessions/<sessionId>/session-state/*/events.jsonl
        $sessionDir = Join-Path $runDir.FullName "sessions/$sessionId"
        $eventsFiles = @(Get-ChildItem -Path $sessionDir -Recurse -Filter 'events.jsonl' -ErrorAction SilentlyContinue)

        if ($eventsFiles.Count -eq 0) {
            Write-Warning "No events.jsonl found for session $sessionId ($skillName / $scenarioName / $role), skipping"
            continue
        }

        $eventsFile = $eventsFiles[0]

        # Build output filename: <scenario>--<role>--run<N>.jsonl
        $safeScenario = ($scenarioName -replace '[^a-zA-Z0-9_-]', '-').ToLower()
        $outFileName = "$safeScenario--$roleTag--run$runIndex.jsonl"

        # Plugin subdirectory
        $pluginOutDir = Join-Path $sessionsOutDir $pluginName
        New-Item -ItemType Directory -Path $pluginOutDir -Force | Out-Null

        $outPath = Join-Path $pluginOutDir $outFileName
        Copy-Item -Path $eventsFile.FullName -Destination $outPath -Force

        $fileSize = (Get-Item $outPath).Length
        Write-Host "  Copied: $pluginName/$outFileName ($([math]::Round($fileSize / 1024, 1)) KB)"

        # Build manifest entry
        $relativeUrl = "sessions/$subDir/$pluginName/$outFileName"
        $displayName = "$pluginName / $scenarioName ($roleTag, run $runIndex)"
        $id = "$subDir/$pluginName/$safeScenario--$roleTag--run$runIndex"

        $tags = @($Source, $pluginName, $roleTag)
        if ($Source -eq 'pr' -and $PrNumber -gt 0) {
            $tags += "pr-$PrNumber"
        }
        if ($Source -eq 'scheduled') {
            $tags += $dateTag
        }

        $mtime = [long]([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())

        $manifestSessions += @{
            id    = $id
            name  = $displayName
            url   = $relativeUrl
            tags  = $tags
            mtime = $mtime
        }
    }
}

# Write manifest.json
$manifest = @{
    generated = (Get-Date -Format 'o')
    sessions  = $manifestSessions
}

$manifestPath = Join-Path $OutputDir "manifest.json"
$manifest | ConvertTo-Json -Depth 10 | Out-File -FilePath $manifestPath -Encoding utf8

Write-Host "`nManifest written to $manifestPath with $($manifestSessions.Count) session(s)"
