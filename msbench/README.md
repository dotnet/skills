# MSBench — Skills Evaluation Benchmark

## What is MSBench?

[MSBench](https://msbenchapp.azurewebsites.net/) is Microsoft's SWE-Bench evaluation platform for assessing AI coding assistants.
It runs agent tasks inside Docker containers on a cloud execution backend (CES),
producing standardised `eval.json` results that feed a leaderboard and historical tracking.

Key components:

| Component | Purpose |
|-----------|---------|
| **msbench-cli** | Submit runs, monitor progress, generate reports |
| **CES** (Code Execution Service) | Azure-hosted Docker execution backend |
| **ACR** | Container registry (`codeexecservice.azurecr.io`) |
| **[msbench-benchmarks](https://dev.azure.com/devdiv/OnlineServices/_git/msbench-benchmarks)** | Benchmark datasets, curation scripts, parquet database |
| **harbor-format-curation** | Converts Harbor-format tasks → MSBench Docker images |
| **Leaderboard** | <https://msbenchapp.azurewebsites.net/> |

For full platform documentation see the [MSBench wiki](https://github.com/devdiv-microsoft/MicrosoftSweBench/wiki).

## What this folder does

The `msbench/` tree contains the **`dotnetskills` benchmark** — a set of
[Harbor-format](https://github.com/devdiv-microsoft/MicrosoftSweBench/wiki/3.-Adding-a-benchmark) tasks
that evaluate whether agent skills (from `plugins/`) actually improve an
agent's ability to solve .NET tasks.

It follows an **A/B pattern** (identical to the existing
[skillsbench / skillsbenchnoskills](https://dev.azure.com/devdiv/OnlineServices/_git/msbench-benchmarks?path=/benchmarks/skillsbench)
benchmark in `msbench-benchmarks`):
tasks are run twice — once with skills loaded and once without —
and the resolve-rate delta shows the real-world value of every skill.

### Opting in to MSBench (`msbench_ready`)

Not every eval.yaml is automatically converted to an MSBench task.
Each eval.yaml must explicitly opt in by setting the **top-level flag**
`msbench_ready: true`:

```yaml
msbench_ready: true          # ← opt-in to MSBench onboarding

scenarios:
  - name: "My scenario"
    prompt: "..."
    assertions: [...]
```

Evals without this flag are silently skipped by the converter.
This keeps the benchmark small and focused while new evals are being
developed and validated locally via the skill-validator.

### Currently onboarded evals

| Eval | Plugin | Scenarios | Why selected |
|------|--------|-----------|--------------|
| `csharp-scripts` | dotnet | 1 | Clear deterministic assertions, proven locally |
| `dotnet-pinvoke` | dotnet | 2 | Pure output-based with 4 strong assertions per scenario |
| `msbuild-modernization` | dotnet-msbuild | 1 | Covers msbuild plugin, uses `copy_test_files` setup |

To onboard additional evals, add `msbench_ready: true` to their
eval.yaml and re-run the converter.

### Excluded skills

5 skills are excluded entirely (regardless of `msbench_ready`) because they
depend on MCP servers not yet available in Docker or require binary artefacts
not suited for containerised execution:
`binlog-failure-analysis`, `binlog-generation`, `build-perf-diagnostics`,
`build-parallelism`, `dump-collect`.

## Folder structure

```
msbench/
├── README.md                  ← you are here
├── dotnetskills.toml          ← harbor-format-curation config (local + prod Docker profiles)
├── version.txt                ← benchmark version (SemVer)
│
├── tasks/                     ← Harbor-format tasks (auto-generated — do not edit by hand)
│   └── <plugin>--<skill>--<slug>/
│       ├── task.toml          ← metadata, tags, difficulty, resource limits
│       ├── instruction.md     ← agent prompt (the problem statement)
│       ├── environment/       ← Docker build context
│       │   ├── Dockerfile     ← .NET SDK image + fixture files + eval helpers
│       │   ├── eval_helpers/  ← assertion_runner.sh, write_eval.py, …
│       │   ├── fixtures/      ← (if the scenario has source fixtures)
│       │   └── test_files/    ← (if the scenario uses copy_test_files)
│       ├── tests/
│       │   └── test.sh        ← evaluation script → writes /output/eval.json
│       └── solution/
│           └── solve.sh       ← stub (gold solutions are authored separately)
│
├── agents/                    ← agent runner packages for the A/B pattern
│   ├── with-skills/           ← Copilot CLI + native skill loading
│   │   ├── runner.sh
│   │   └── config.yaml
│   └── without-skills/        ← Copilot CLI baseline (no skills)
│       ├── runner.sh
│       └── config.yaml
│
├── shared/
│   └── eval_helpers/          ← reusable evaluation scripts (copied into every task)
│       ├── assertion_runner.sh   ← bash assertion framework (source in test.sh)
│       ├── write_eval.py         ← generates eval.json + custom_metrics.json
│       ├── parse_build.py        ← parse dotnet build output
│       ├── parse_trx.py          ← parse .trx test-result files
│       └── check_pattern.py      ← configurable grep-based pattern checks
│
└── scripts/
    ├── convert_evals.py          ← converter: eval.yaml → Harbor tasks
    ├── validate_tasks.py         ← E2E structural validation
    ├── analyze_results.py        ← post-run A/B comparison report
    ├── prepare_agent_packages.sh ← copies in-scope SKILL.md files into agent package
    └── test_convert_evals.py     ← unit tests for the converter (pytest)
```

## How tasks are generated

Tasks are **not written by hand**. They are converted from the existing
`eval.yaml` files that live alongside each evaluation scenario under `tests/`
(e.g. `tests/dotnet/csharp-scripts/eval.yaml`).
The converter reads every `eval.yaml` **that has `msbench_ready: true`**,
maps each scenario to a Harbor task directory, resolves fixture paths,
generates the Dockerfile, test.sh, etc. Evals without the flag are skipped.

```powershell
# Regenerate all tasks from eval.yaml sources
python msbench/scripts/convert_evals.py `
    --skills-dir plugins/ `
    --tests-dir tests/ `
    --output-dir msbench/tasks/
```

Useful converter modes:

| Flag | Behaviour |
|------|-----------|
| *(none)* | Generate / overwrite all task directories |
| `--dry-run` | Print what *would* be generated without writing files |
| `--check` | Verify existing tasks are in sync with eval.yaml; exit non-zero on drift |

After regenerating, validate:

```powershell
python msbench/scripts/validate_tasks.py `
    --tasks-dir msbench/tasks/ `
    --tests-dir tests/ `
    --skills-dir plugins/
```

## Local usage

### Prerequisites

- Python 3.10+ with `pyyaml` installed (`pip install pyyaml`)
- Docker (for building / running images locally)
- `msbench-cli` installed ([MicrosoftSweBench](https://dev.azure.com/devdiv/InternalTools/_git/MicrosoftSweBench))

### 1. Generate and validate tasks

```powershell
# From the repo root
python msbench/scripts/convert_evals.py --skills-dir plugins/ --tests-dir tests/ --output-dir msbench/tasks/
python msbench/scripts/validate_tasks.py --tasks-dir msbench/tasks/ --tests-dir tests/ --skills-dir plugins/
```

### 2. Build Docker images locally

Use `harbor-format-curation` (from the `msbench-benchmarks` repo) pointed at
`dotnetskills.toml` with the `docker.local` profile:

```bash
# Inside the msbench-benchmarks repo (with harbor-format-curation installed)
harbor-curation build \
    --config /path/to/skills/msbench/dotnetskills.toml \
    --profile local \
    --tasks-dir /path/to/skills/msbench/tasks/
```

This builds images tagged like
`localhost:5000/dotnetskills.eval.x86_64.<task-name>:msbench-0.1.0`.

### 3. Run a single task manually

```bash
docker run --rm \
    -v /tmp/output:/output \
    localhost:5000/dotnetskills.eval.x86_64.<task-name>:msbench-0.1.0
```

The container produces `/output/eval.json` with the result.

### 4. Submit via msbench-cli (against CES)

```bash
# With skills
msbench-cli run submit \
    --benchmark dotnetskills \
    --agent-dir msbench/agents/with-skills/ \
    --tag skills=enabled

# Without skills (baseline)
msbench-cli run submit \
    --benchmark dotnetskills \
    --agent-dir msbench/agents/without-skills/ \
    --tag skills=disabled
```

### 5. Compare results

After both runs complete, download the results and diff them:

```bash
python msbench/scripts/analyze_results.py \
    --with-skills results/with-skills/ \
    --without-skills results/without-skills/
```

This prints a report showing per-task, per-skill, and overall resolve-rate
delta between the two runs.

### Running unit tests

```powershell
python -m pytest msbench/scripts/test_convert_evals.py -v
```

## Pipeline usage (CI/CD)

The benchmark is designed to run in Azure Pipelines. A typical pipeline does:

1. **Generate & validate** — run the converter in `--check` mode to ensure
   tasks are in sync; fail the build if they drift.
2. **Build images** — invoke `harbor-format-curation` with the `docker.prod`
   profile to build and push images to ACR
   (`codeexecservice.azurecr.io`).
3. **Submit A/B runs** — use `msbench-cli run submit` twice (with and
   without skills), blocking until both complete.
4. **Analyse** — run `analyze_results.py` on the two result sets and
   publish the summary as a pipeline artefact.

### Pipeline-specific environment

| Variable | Purpose |
|----------|---------|
| `BENCHMARK_PARQUET_PATH` | Path to the benchmark parquet (set by the msbench-benchmarks package) |
| `CES_ENVIRONMENT` | CES backend to target (`ces-dev1`, `ces-staging`, `ces-ame`) |

### Sync-check gate

Add this as an early pipeline step to fail fast if someone edits an
`eval.yaml` without regenerating the Harbor tasks:

```yaml
- script: |
    python msbench/scripts/convert_evals.py \
        --skills-dir plugins/ \
        --tests-dir tests/ \
        --output-dir msbench/tasks/ \
        --check
  displayName: "Verify Harbor tasks are in sync with eval.yaml"
```

## Versioning

The benchmark version lives in `version.txt` and follows SemVer:

- **Patch** — refresh task content (re-run converter after eval.yaml edits)
- **Minor** — add new skills or tasks
- **Major** — breaking changes to the evaluation schema

Image tags follow the pattern
`dotnetskills.eval.x86_64.<task-name>:msbench-<version>`.
