# Fix: Copilot API 429 Rate Limiting in Evaluation Pipeline

## Problem Summary

Since PR [#274](https://github.com/dotnet/skills/pull/274) (merged 2026-03-07) reorganized the single `dotnet` plugin into 5 domain-specific plugins (`dotnet`, `dotnet-data`, `dotnet-diag`, `dotnet-msbuild`, `dotnet-upgrade`), scheduled evaluation runs fail at a 69% rate. The `dotnet-data` and `dotnet-diag` jobs consistently crash with a **429 rate limit error** from the Copilot API before any evaluation begins.

**Error (from run #22810527011):**
```
Failed to validate model: System.IO.IOException: Communication error with Copilot CLI:
Request models.list failed with message: Failed to list models: 429
```

**Root cause chain:**
1. The evaluation matrix now spawns **5 parallel jobs** (one per plugin) instead of 1.
2. Each job randomly selects one of 8 Copilot tokens and immediately calls `ListModelsAsync()` to validate the model.
3. With 5 jobs starting simultaneously, multiple jobs may pick the same token and hit the Copilot API `models.list` endpoint concurrently, triggering a 429.
4. `ListModelsAsync()` in `ValidateCommand.Run()` has **zero retry logic** — any exception immediately exits with code 1.

## Analysis

### Where retry exists today

| Call site | Retry? | Mechanism |
|---|---|---|
| `Judge.JudgeRun()` | Yes | `RetryHelper.ExecuteWithRetry` (2 retries, 5s base backoff, 10min budget) |
| `PairwiseJudge.JudgeOnce()` | Yes | `RetryHelper.ExecuteWithRetry` |
| `OverfittingJudge.Analyze()` | Yes | Manual retry loop (2 retries) |
| **`ValidateCommand.Run()` → `ListModelsAsync()`** | **No** | Bare `await` in try/catch that returns exit code 1 |
| `AgentRunner.RunAgent()` | No | No retry |

### Concurrency amplification

The workflow passes `--parallel-skills 5 --parallel-scenarios 5 --parallel-runs 5` for scheduled runs. Each matrix job can spawn up to **5 × 5 × 5 = 125** concurrent agent sessions (×2 for baseline + skilled = 250 Copilot sessions). With 5 jobs running in parallel, the theoretical max is **1,250** concurrent Copilot API calls across the workflow, all potentially sharing the same token pool.

For infra changes, concurrency is reduced to `2 × 3 × 3`, but **scheduled runs (where all plugins are evaluated) use the full `5 × 5 × 5`** — the same path that's failing.

## Recommended Fix (multi-layered)

### 1. **[Critical] Add retry with backoff to `ListModelsAsync()` — the immediate fix**

Wrap the `ListModelsAsync()` call in `ValidateCommand.Run()` with `RetryHelper.ExecuteWithRetry`. This is the simplest, most targeted change and directly addresses the 429 crash.

**File:** `eng/skill-validator/src/Commands/ValidateCommand.cs` (line ~124)

```csharp
// Before (no retry):
var client = await AgentRunner.GetSharedClient(config.Verbose);
var models = await client.ListModelsAsync();

// After (with retry):
var client = await AgentRunner.GetSharedClient(config.Verbose);
var models = await RetryHelper.ExecuteWithRetry(
    async ct => await client.ListModelsAsync(),
    label: "ListModels",
    maxRetries: 3,
    baseDelayMs: 2_000,
    totalTimeoutMs: 60_000);
```

**Why this is the best first fix:** The `RetryHelper` already exists, is well-tested, and handles exponential backoff with budget caps. A 429 on `models.list` is transient — a 2–8 second wait almost certainly succeeds on retry.

### 2. **[Important] Reduce scheduled-run parallelism to match infra-change settings**

The `evaluation.yml` workflow already reduces concurrency for infra changes but uses full parallelism for schedule triggers. Since scheduled runs also evaluate all 5 plugins in parallel, they should use the same reduced settings.

**File:** `.github/workflows/evaluation.yml` (line ~140)

```yaml
# Before: schedule uses default '5' for all three
parallel-skills: ${{ needs.discover.outputs.is_infra == 'true' && '2' || '5' }}

# After: reduce for both infra and schedule
parallel-skills: ${{ (needs.discover.outputs.is_infra == 'true' || github.event_name == 'schedule') && '2' || '5' }}
parallel-scenarios: ${{ (needs.discover.outputs.is_infra == 'true' || github.event_name == 'schedule') && '3' || '5' }}
parallel-runs: ${{ (needs.discover.outputs.is_infra == 'true' || github.event_name == 'schedule') && '3' || '5' }}
```

### 3. **[Important] Shard tokens across matrix jobs deterministically**

Currently each job picks a random token from the populated-token pool (5 of 8 slots are populated; the selection script correctly filters out empty secrets via `-n` check). With 5 jobs each independently calling `RANDOM % 5`, the probability that all 5 pick distinct tokens is only **5!/5⁵ ≈ 3.8%** — meaning **~96% of the time at least two jobs share a token**, directly amplifying the 429 risk.

Instead, assign tokens by matrix index:

```bash
# Instead of RANDOM:
IDX=$(( ${{ strategy.job-index }} % ${#TOKENS[@]} ))
```

With 5 populated tokens and 5 matrix jobs, this guarantees every job gets a unique token — completely eliminating token-collision-induced rate limits.

### 4. **[Future] Add retry to `AgentRunner.RunAgent()` for mid-eval 429s**

The `RunAgent` call (which runs actual Copilot sessions) also lacks retry logic. While the judging calls are wrapped with `RetryHelper`, the agent execution itself is not. Once the initial rate limit is fixed, mid-evaluation 429s could become the next failure mode under high load.

## Recommended Implementation Order

| Priority | Change | Effort | Impact |
|---|---|---|---|
| **P0** | Add retry to `ListModelsAsync()` | ~10 lines | Fixes the immediate crash |
| **P1** | Reduce scheduled-run parallelism | ~3 lines in YAML | Prevents overload at the source |
| **P2** | Deterministic token sharding | ~5 lines in YAML | Eliminates token collision (~96% chance today) |
| **P3** | Retry on `RunAgent()` | Medium | Resilience against mid-eval 429s |

**P0 alone is sufficient to unblock the pipeline.** The 429 on `models.list` is a brief transient error — even a single retry with 2-second delay would resolve it. P1 + P2 reduce the probability of hitting the rate limit at all.
