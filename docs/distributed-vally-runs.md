# Distributed Vally eval runbook

Date: 2026-07-10
Branch: `dev/yuanfe/multi-model-skill-validation`

This document is for running the remaining Vally eval matrix on a second machine without duplicating work already running on the primary machine.

## Current snapshot

Coverage was computed across all local result roots matching `vally-results*` and `ci-vally-artifacts-*`, not just the newest result folder.

- Target models: `claude-opus-4.8`, `claude-sonnet-4.6`, `gpt-5.5`
- Eligible suites: 94
- Expected target-model jobs: 282
- Completed target-model jobs found locally: 193
- Missing target-model jobs: 89

Missing jobs by plugin at the time of this snapshot:

```text
dotnet-aspnetcore        1
dotnet-diag              9
dotnet-experimental      7
dotnet-maui             24
dotnet-msbuild          13
dotnet-nuget             3
dotnet-template-engine  18
dotnet-test             14
```

Important: older `dotnet-msbuild` results are spread across earlier `vally-results-rerun-20260707-*` folders. Do not count only the newest `vally-results-rerun-20260708-timeout30m-runs5-all-v1` folder or `dotnet-msbuild` will look much more incomplete than it is.

## Prerequisites on the new machine

1. Fetch this branch after the commit has been pushed:

   ```bash
   git fetch origin
   git switch dev/yuanfe/multi-model-skill-validation || git switch -c dev/yuanfe/multi-model-skill-validation origin/dev/yuanfe/multi-model-skill-validation
   ```

2. Run from Git Bash at the repository root.

3. Make sure auth and tools are available:

   ```bash
   test -n "$GITHUB_TOKEN" || echo "Set GITHUB_TOKEN before running evals"
   node --version
   npm --version
   dotnet --info
   ```

4. Use a unique results directory per machine. Result folders are ignored by `.gitignore` via `vally-results*/`, so they should not pollute `git status`.

## Machine B assignment

The primary machine was already running diagnostics, MAUI, and test jobs when this handoff was written. Machine B should run this non-overlapping 42-job set first:

- `dotnet-aspnetcore`: 1 job
- `dotnet-experimental`: 7 jobs
- `dotnet-msbuild`: 13 remaining jobs
- `dotnet-nuget`: 3 jobs
- `dotnet-template-engine`: 18 jobs

Use this command block from Git Bash:

```bash
export RUNS=5
export WORKERS=1
export PARALLEL=1
export RESULTS_DIR="$PWD/vally-results-rerun-20260710-machine-b-runs5-v1"
export VALLY="npx --yes --registry=https://registry.npmjs.org/ @microsoft/vally-cli@0.7.0"
export VALLY_EXTRA_ARGS="--timeout 30m --max-retries 0 --shutdown-timeout 60s"

run_job() {
  local model="$1"
  local plugin="$2"
  local skill="$3"
  echo "=== $plugin/$skill @ $model ==="
  MODEL="$model" ./eng/vally-adapter/run-vally-evals.sh "$plugin" "$skill"
}

while read -r model plugin skill; do
  [ -z "$model" ] && continue
  run_job "$model" "$plugin" "$skill"
done <<'JOBS'
gpt-5.5 dotnet-aspnetcore dotnet-webapi
claude-opus-4.8 dotnet-experimental exp-mock-usage-analysis
gpt-5.5 dotnet-experimental exp-mock-usage-analysis
claude-opus-4.8 dotnet-experimental exp-simd-vectorization
claude-sonnet-4.6 dotnet-experimental exp-simd-vectorization
gpt-5.5 dotnet-experimental exp-simd-vectorization
claude-sonnet-4.6 dotnet-experimental exp-test-maintainability
gpt-5.5 dotnet-experimental exp-test-maintainability
gpt-5.5 dotnet-msbuild check-bin-obj-clash
claude-opus-4.8 dotnet-msbuild extension-points
claude-sonnet-4.6 dotnet-msbuild extension-points
gpt-5.5 dotnet-msbuild extension-points
claude-opus-4.8 dotnet-msbuild item-management
claude-sonnet-4.6 dotnet-msbuild item-management
gpt-5.5 dotnet-msbuild item-management
claude-opus-4.8 dotnet-msbuild property-patterns
claude-sonnet-4.6 dotnet-msbuild property-patterns
gpt-5.5 dotnet-msbuild property-patterns
claude-opus-4.8 dotnet-msbuild target-authoring
claude-sonnet-4.6 dotnet-msbuild target-authoring
gpt-5.5 dotnet-msbuild target-authoring
claude-opus-4.8 dotnet-nuget convert-to-cpm
claude-sonnet-4.6 dotnet-nuget convert-to-cpm
gpt-5.5 dotnet-nuget convert-to-cpm
claude-opus-4.8 dotnet-template-engine template-authoring
claude-sonnet-4.6 dotnet-template-engine template-authoring
gpt-5.5 dotnet-template-engine template-authoring
claude-opus-4.8 dotnet-template-engine template-comparison
claude-sonnet-4.6 dotnet-template-engine template-comparison
gpt-5.5 dotnet-template-engine template-comparison
claude-opus-4.8 dotnet-template-engine template-discovery
claude-sonnet-4.6 dotnet-template-engine template-discovery
gpt-5.5 dotnet-template-engine template-discovery
claude-opus-4.8 dotnet-template-engine template-instantiation
claude-sonnet-4.6 dotnet-template-engine template-instantiation
gpt-5.5 dotnet-template-engine template-instantiation
claude-opus-4.8 dotnet-template-engine template-smart-defaults
claude-sonnet-4.6 dotnet-template-engine template-smart-defaults
gpt-5.5 dotnet-template-engine template-smart-defaults
claude-opus-4.8 dotnet-template-engine template-validation
claude-sonnet-4.6 dotnet-template-engine template-validation
gpt-5.5 dotnet-template-engine template-validation
JOBS
```

## Check progress on Machine B

Count adapted results:

```bash
find "$RESULTS_DIR" -name results.json -type f | wc -l
```

Expected count for the Machine B assignment is 42 `results.json` files.

Check for the old unsupported grader failure:

```bash
grep -R "Unknown grader type" "$RESULTS_DIR" || true
```

That command should print nothing. This branch removes Vally's unsupported `pairwise` graders from the eval YAML files while keeping the `prompt` grader and rubric text.

## Return results to the primary machine

Do not commit the result folder. It is intentionally ignored.

Archive the Machine B result directory and copy it back to the primary machine:

```bash
tar -czf vally-results-rerun-20260710-machine-b-runs5-v1.tgz -C "$(dirname "$RESULTS_DIR")" "$(basename "$RESULTS_DIR")"
```

On the primary machine, extract it at the repository root next to the other `vally-results*` directories, then recompute coverage across all local result roots.

## Notes

- Each job runs both baseline and skilled legs with `RUNS=5`.
- `WORKERS=1` is intentional for reliability on long-running evals.
- The script deletes only the per-eval baseline/skilled subfolders under the configured `RESULTS_DIR`, so using a unique `RESULTS_DIR` avoids collisions with other machines.
- If a job fails or times out, rerun the same single job command with the same `RESULTS_DIR`.