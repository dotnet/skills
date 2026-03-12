# Design: Load Whole Plugin Instead of Single Skill

## Problem Statement

The skill validator evaluates whether individual skills improve agent performance. Previously, it loaded **only the skill under test** via `SessionConfig.SkillDirectories`. In production, users load entire **plugins** (all skills, agents, MCP servers via `plugin.json`). This meant the validator evaluated skills in unrealistic isolation — cross-skill interactions were never tested.

## Three-Run Evaluation Model

Every skill evaluation performs **three concurrent agent runs**:

| Run Type | Skills Loaded | Client | Purpose |
|----------|--------------|--------|---------|
| **Baseline** | None (`SkillDirectories = []`) | No-plugin client | Vanilla agent reference |
| **Skilled-Isolated** | Only the skill under test (`SkillDirectories = [skillParent]`) | No-plugin client | Isolated skill improvement (original behavior) |
| **Skilled-Plugin** | Entire plugin via `--plugin-dir` | Per-plugin client | Production-realistic — skill within full plugin context |

The **final verdict** uses `min(skilled-isolated, skilled-plugin)` — a skill passes only if it performs well both in isolation and within its plugin context.

## Architecture

> All file paths below are relative to `eng/skill-validator/src/` unless noted otherwise.

### Plugin Loading via `SkillDirectories` (Manual Enumeration)

~~Plugins were originally loaded via the CLI's `--plugin-dir` flag through `CopilotClientOptions.CliArgs`. However, `--plugin-dir` is **not honored by the SDK**.~~

Instead, plugin skills are loaded **manually**: `BuildSessionConfig` reads `plugin.json`, resolves the plugin's `skills` path, and passes that directory via `SessionConfig.SkillDirectories`. The SDK scans it for subdirectories containing `SKILL.md` files — the same code path used for isolated skill loading, just pointing at the entire plugin's skills directory instead of a single skill's parent.

MCP servers from `plugin.json` are also passed through `SessionConfig.McpServers` (resolved during skill discovery via `FindPluginMcpServers`).

### Shared CopilotClient

Since `--plugin-dir` is not used, **all runs share the same `CopilotClient`**. There is no per-plugin client pool. Baseline, skilled-isolated, and skilled-plugin runs all use `GetSharedClient()`. The difference between run types is entirely in `SessionConfig` (what `SkillDirectories` and `McpServers` are set to).

**File**: `Services/AgentRunner.cs`
- `GetSharedClient(bool verbose)`: Returns the shared no-plugin client; used by all runs
- `ResolvePluginSkillDirectories(string pluginRoot)`: Reads `plugin.json`, resolves skills path, returns it for `SkillDirectories`
- `StopAllClients()`: Stops all clients at shutdown
- `CaptureGitHubToken()`: Captures token once at startup, passes to all clients via `CopilotClientOptions.GitHubToken`

### Session Config (`BuildSessionConfig`)

**File**: `Services/AgentRunner.cs`

Signature: `BuildSessionConfig(SkillInfo? skill, string? pluginRoot, string model, string workDir, ...)`

| Parameter combination | Run type | `SkillDirectories` | `McpServers` |
|----------------------|----------|-------------------|-------------|
| `skill=null, pluginRoot=null` | Baseline | `[]` | `null` |
| `skill=X, pluginRoot=null` | Skilled-isolated | `[skillPath]` | From skill's `McpServers` |
| `skill=X, pluginRoot=Y` | Skilled-plugin | `[resolvedSkillsDir]` | From skill's `McpServers` |

`resolvedSkillsDir` is computed by `ResolvePluginSkillDirectories`: reads `plugin.json`, resolves the `skills` path relative to the plugin root, and returns it. The SDK scans this directory for subdirectories containing `SKILL.md`.

Each session gets a unique `ConfigDir` (temp directory) for isolation.

### Permission Checking

**File**: `Services/AgentRunner.cs` — `CheckPermission(request, workDir, skillPath, pluginRoot)`

Allows access to `workDir`, `skillPath`, and `pluginRoot` directories. The `pluginRoot` parameter was added so the agent can read sibling skills from the plugin during plugin runs.

### Orchestration

**File**: `Commands/ValidateCommand.cs`

1. **Discovery**: `SkillDiscovery.GroupSkillsByPlugin(allSkills)` groups skills by plugin root. Skills without a `plugin.json` ancestor are errors.
2. **Client creation**: Pre-creates a `CopilotClient` per plugin with `--plugin-dir`.
3. **Execution** (`ExecuteRun`): Launches 3 concurrent `RunAgent` calls, evaluates assertions/constraints on all 3, judges all 3 independently, runs pairwise judge on baseline vs. worse-scoring skilled run.
4. **Aggregation** (`ExecuteScenario`): Averages N runs per type, computes `min(isolated, plugin)` per-run scores for confidence intervals.
5. **Verdict** (`EvaluateSkill`): Checks activation in both skilled runs independently — fails if either is not activated.

### Scoring

**File**: `Services/Comparator.cs`

- `CompareScenario` is unchanged — called twice (baseline vs. isolated, baseline vs. plugin)
- `ComputeVerdict` exposes both `IsolatedScore` and `PluginScore` on `SkillVerdict`
- Effective `ImprovementScore` is `min(isolated, plugin)` per scenario
- `ComputeNormalizedGain` uses `min(isolatedQuality, pluginQuality)` for the final gain calculation

### Models

**File**: `Models/Models.cs`

```csharp
public sealed class ScenarioComparison
{
    public required RunResult Baseline { get; init; }
    public required RunResult SkilledIsolated { get; init; }
    public RunResult? SkilledPlugin { get; init; }          // nullable for CompareScenario output
    public double ImprovementScore { get; init; }           // min(isolated, plugin)
    public double IsolatedImprovementScore { get; init; }
    public double PluginImprovementScore { get; init; }
    public SkillActivationInfo? SkillActivationIsolated { get; set; }
    public SkillActivationInfo? SkillActivationPlugin { get; set; }
    // ... other fields
}

public sealed class SkillVerdict
{
    public double? IsolatedScore { get; set; }   // avg isolated improvement
    public double? PluginScore { get; set; }     // avg plugin improvement
    // ... other fields
}
```

### Reporting & Dashboard

**File**: `Services/Reporter.cs` — Console output shows three columns (baseline, isolated, plugin) with an "Effective score: min(...)" summary line.

**File**: `eng/dashboard/dashboard.js` — `createTripleChart` renders 3 lines per skill (gray=baseline, blue=isolated, green=plugin).

**File**: `eng/dashboard/generate-benchmark-data.ps1` — Emits three data series per skill.

## Targeted Skill Activation Detection (Implemented)

**File**: `Services/MetricsCollector.cs`

`ExtractSkillActivation` accepts an optional `targetSkillName` parameter. When set (as it is for both isolated and plugin runs), only events matching the target skill count towards activation — preventing sibling-skill false positives in plugin runs. Callers in `ValidateCommand.ExecuteRun` pass `skill.Name` for both the isolated and plugin activation checks. The `ExtraTools` heuristic remains as a fallback for skills that don't emit `SkillInvokedEvent`.

## Execution Flow

```
ValidateCommand.Run
├── DiscoverSkills
├── GroupSkillsByPlugin → { pluginRoot → (plugin, [skills]) }
├── GetSharedClient()                         → used by judge & baseline runs
│
├── for each skill (parallel by --parallel-skills):
│   └── EvaluateSkill
│       └── for each scenario (parallel by --parallel-scenarios):
│           └── ExecuteScenario
│               └── for each run (parallel by --parallel-runs):
│                   ├── RunAgent(baseline)         → shared client, SkillDirectories=[]
│                   ├── RunAgent(skilled-isolated)  → shared client, SkillDirectories=[skillParent]
│                   ├── RunAgent(skilled-plugin)    → shared client, SkillDirectories=[pluginSkillsDir]
│                   ├── Judge all 3 independently
│                   ├── PairwiseJudge: baseline vs. worse-scoring skilled
│                   └── Activation check on both skilled runs
│
└── StopAllClients + CleanupWorkDirs
```

## Edge Cases

1. **Standalone skills (no plugin)**: Treated as errors by `GroupSkillsByPlugin`. All skills in this repository are under `plugins/*/skills/`.

2. **Multiple plugins**: Each plugin's skills are resolved independently via `ResolvePluginSkillDirectories`. All share the same client.

3. **Cost**: 4 judge calls per scenario run (baseline + isolated + plugin + pairwise) — 2× the previous cost.

4. **Resource overhead**: Single CLI process shared by all runs.
