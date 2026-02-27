# skill-validator

You've built a bunch of skills. But are they actually helping or just adding noise?

**skill-validator** finds out. It runs your agent with and without each skill, measures what changed, and tells you whether the skill is worth keeping.

Plugging into your CI, it ensures every new skill adds real value, and existing skills that stop helping when a new model comes out can be removed.

## How it works

1. Discovers skills (directories with `SKILL.md`)
2. Reads evaluation scenarios from each skill's `tests/eval.yaml`
3. For each scenario, runs the agent **without** the skill (baseline) and **with** the skill
4. Collects metrics: token usage, tool calls, time, errors, task completion
5. Uses **pairwise comparative judging** — the LLM judge sees both outputs side-by-side and decides which is better (with position-swap bias mitigation)
6. Computes **confidence intervals** via bootstrapping across multiple runs
7. Compares results and produces a verdict with statistical significance
8. Saves detailed results (JSON + markdown) to `.skill-validator-results/`

## Prerequisites

- .NET 10 SDK or later
- Authenticated with GitHub via `gh auth login` (the SDK picks up your credentials automatically)

## Build

```bash
cd eng/skill-validator
dotnet build
```

## Usage

```bash
# Validate all skills in a directory
dotnet run --project src/SkillValidator -- ./path/to/skills/

# Validate a single skill
dotnet run --project src/SkillValidator -- ./path/to/my-skill/

# Verbose output with per-scenario breakdowns
dotnet run --project src/SkillValidator -- --verbose ./skills/

# Custom model and threshold
dotnet run --project src/SkillValidator -- --model claude-sonnet-4.5 --min-improvement 0.2 ./skills/

# Use a different model for judging vs agent runs
dotnet run --project src/SkillValidator -- --model gpt-5.3-codex --judge-model claude-opus-4.6-fast ./skills/

# Multiple runs for stability
dotnet run --project src/SkillValidator -- --runs 5 ./skills/

# Override the default results directory (.skill-validator-results)
dotnet run --project src/SkillValidator -- --results-dir ./my-results ./skills/

# File reporters can also be specified explicitly.
dotnet run --project src/SkillValidator -- --reporter junit ./skills/

# Require all skills to have evals
dotnet run --project src/SkillValidator -- --require-evals ./skills/

# Verdict-warn-only mode (verdict failures return exit 0, execution errors still fail)
dotnet run --project src/SkillValidator -- --verdict-warn-only --require-evals ./skills/
```

## Writing eval files

Each skill can include a `tests/eval.yaml`:

```yaml
scenarios:
  - name: "Descriptive name of the scenario"
    prompt: "The prompt to send to the agent"
    setup:
      copy_test_files: true  # auto-copy all sibling files into agent work dir
      files:
        - path: "input.txt"
          content: "file content to create before the run"
        - path: "data.csv"
          source: "fixtures/sample-data.csv"  # relative to skill dir
    assertions:
      - type: "output_contains"
        value: "expected text"
      - type: "output_not_contains"
        value: "text that should not appear"
      - type: "output_matches"
        pattern: "regex pattern"
      - type: "output_not_matches"
        pattern: "regex that should not match"
      - type: "file_exists"
        path: "*.csv"
      - type: "file_not_exists"
        path: "*.csproj"
      - type: "file_contains"
        path: "*.cs"
        value: "stackalloc"
      - type: "exit_success"
    expect_tools: ["bash"]
    reject_tools: ["create_file"]
    max_turns: 15
    rubric:
      - "The output is well-formatted and clear"
      - "The agent correctly handled edge cases"
    timeout: 120
```

### Assertion types

| Type | Description |
|------|-------------|
| `output_contains` | Agent output contains `value` (case-insensitive) |
| `output_not_contains` | Agent output does NOT contain `value` |
| `output_matches` | Agent output matches `pattern` (regex) |
| `output_not_matches` | Agent output does NOT match `pattern` |
| `file_exists` | File matching `path` glob exists in work dir |
| `file_not_exists` | No file matching `path` glob exists in work dir |
| `file_contains` | File matching `path` glob contains `value` |
| `exit_success` | Agent produced non-empty output |

### Setup options

| Option | Description |
|--------|-------------|
| `copy_test_files` | When `true`, copies all files from the eval directory (except `eval.yaml`) into the agent working directory before each run. Useful when test fixtures live alongside the eval file. |
| `files` | Explicit list of files to create in the working directory. Each entry has a `path` and either inline `content` or a `source` path (relative to the skill directory). Applied after `copy_test_files`. |

### Scenario constraints

Constraints are declarative checks against run metrics — no regex or globs needed:

```yaml
scenarios:
  - name: "Test C# scripting"
    prompt: "Test stackalloc with nint"
    expect_tools: ["bash"]           # agent must use these tools
    reject_tools: ["create_file"]    # agent must NOT use these tools
    max_turns: 10                    # agent must finish within N turns
    max_tokens: 5000                 # agent must use fewer than N tokens
```

| Constraint | Description |
|-----------|-------------|
| `expect_tools` | List of tool names the agent must use |
| `reject_tools` | List of tool names the agent must NOT use |
| `max_turns` | Maximum number of agent turns allowed |
| `max_tokens` | Maximum token usage allowed |

Constraints are evaluated alongside assertions — a failed constraint means a failed task.

### Rubric

Rubric items are evaluated by an LLM judge that sees both the baseline and skill-enhanced outputs side-by-side (pairwise mode). The judge determines which output is better per criterion and by how much. Position bias is mitigated by running the comparison twice with swapped order and checking consistency.

In independent mode, rubric items are scored 1–5 per run. Quality metrics have the highest weight (0.70 combined) in the improvement score.

## Skill profile analysis

Before running the A/B evaluation, skill-validator performs static analysis of each SKILL.md and reports a one-line profile:

```
📊 crank-benchmarking: 1,722 tokens (detailed ✓), 29 sections, 24 code blocks
   ⚠  No numbered workflow steps detected
```

This is grounded in [SkillsBench](https://arxiv.org/abs/2602.12670) findings (84 tasks, 7,308 trajectories):
- **"Detailed" and "compact" skills work best** (+18.8pp and +17.1pp improvement)
- **"Comprehensive" skills hurt performance** (–2.9pp) — long documents create cognitive overhead
- **Sweet spot is 800–2,500 tokens** (ecosystem median: 1,569 tokens)
- **2–3 focused skills outperform 4+** skills bundled together

When a skill fails validation, the profile warnings appear in the diagnosis to suggest what to fix.

## Metrics & scoring

The improvement score is a weighted sum. Quality is heavily prioritized — a skill that improves output quality will pass even if it uses more tokens:

| Metric | Weight | What it measures |
|--------|--------|------------------|
| Quality (rubric) | 0.40 | Pairwise rubric comparison (or independent judge rubric scores) |
| Quality (overall) | 0.30 | Pairwise overall comparison (or independent judge holistic assessment) |
| Task completion | 0.15 | Did hard assertions pass? |
| Token reduction | 0.05 | Fewer tokens = more efficient |
| Error reduction | 0.05 | Fewer errors/retries |
| Tool call reduction | 0.025 | Fewer tool calls = more efficient |
| Time reduction | 0.025 | Faster completion |

All efficiency metrics are clamped to [-1, 1] so extreme changes can't overwhelm quality gains.

A skill **passes** if its average improvement score across scenarios meets the threshold (default 10%).

### Pairwise judging

By default (`--judge-mode pairwise`), the LLM judge sees both baseline and skill-enhanced outputs in a single prompt and makes a direct comparison. This is more reliable than independent scoring because:

- LLMs are better at relative comparison than absolute scoring
- Eliminates calibration drift between separate judge calls
- Directly answers "is the skill-enhanced version better?"

**Position-swap bias mitigation**: Each comparison is run twice — once with baseline first, once with skill first. If the judge picks the same winner in both orderings, the result is trusted. If it flips, the comparison defaults to a tie (flagged as inconsistent).

### Overfitting detection

An eval can be "overfitted" — rewarding the agent for parroting the skill's specific phrasing rather than producing a genuinely better result. The overfitting judge runs **in parallel** with scenario execution (one LLM call per skill) and classifies each rubric item and assertion:

- **outcome** / **broad** — tests a correct result; not overfitted
- **technique** / **narrow** — tests a skill-specific method; moderately overfitted
- **vocabulary** — tests the skill's exact wording; highly overfitted

The per-element classifications are combined into a 0–1 score (✅ low ≤ 0.20, 🟡 moderate ≤ 0.50, 🔴 high). The score is **informational only** — it does not affect the pass/fail verdict. It appears in console output, the markdown results table, and the dashboard.

Use `--overfitting-fix` to generate an `eval.fixed.yaml` with suggested outcome-focused replacements for flagged items. Disable the check entirely with `--no-overfitting-check`.

See [OverfittingDetection.md](OverfittingDetection.md) for the full design.

### Statistical confidence

Results include bootstrap confidence intervals computed across individual runs. The output shows:

```
✓ my-skill  +18.5%  [+8.2%, +28.8%] significant  (g=+24.3%)
✗ other-skill  +6.3%  [-2.1%, +14.7%] not significant  (g=+8.1%)
```

- **significant**: the 95% CI doesn't cross zero — the improvement is real
- **not significant**: the CI crosses zero — could be noise
- **g=**: normalized gain, controlling for ceiling effects (a skill improving a strong baseline is harder than improving a weak one)

The default of 5 runs provides sufficient precision for significance testing (validated by [SkillsBench](https://arxiv.org/abs/2602.12670)).

## CLI flags

| Flag | Default | Description |
|------|---------|-------------|
| `--model <name>` | `claude-opus-4.6` | Model for agent runs |
| `--judge-model <name>` | same as `--model` | Model for LLM judge (can be different) |
| `--judge-mode <mode>` | `pairwise` | Judge mode: `pairwise`, `independent`, or `both` |
| `--min-improvement <n>` | `0.1` | Minimum improvement score (0–1) |
| `--runs <n>` | `5` | Runs per scenario (averaged for stability) |
| `--parallel-skills <n>` | `1` | Max concurrent skills to evaluate |
| `--parallel-scenarios <n>` | `1` | Max concurrent scenarios per skill |
| `--parallel-runs <n>` | `1` | Max concurrent runs per scenario |
| `--confidence-level <n>` | `0.95` | Confidence level for statistical intervals (0–1) |
| `--judge-timeout <n>` | `300` | Judge LLM timeout in seconds |
| `--require-completion` | `true` | Fail if skill regresses task completion |
| `--require-evals` | `false` | Fail if skill has no tests/eval.yaml |
| `--verdict-warn-only` | `false` | Treat verdict failures as warnings (exit 0). Execution errors and `--require-evals` still fail. |
| `--no-overfitting-check` | `false` | Disable the LLM-based overfitting analysis (on by default) |
| `--overfitting-fix` | `false` | Generate `eval.fixed.yaml` with improved rubric items/assertions |
| `--verbose` | `false` | Show tool calls and agent events during runs |
| `--reporter <spec>` | `console`, `json`, `markdown` | Output format: `console`, `json`, `junit`, `markdown`. |
| `--results-dir <path>` | `.skill-validator-results` | Directory for file reporter output. |

Models are validated on startup — invalid model names fail fast with a list of available models.

## Output

Results are displayed in the console with color-coded scores and metric deltas. By default, `json` and `markdown` reporters are enabled and write to `.skill-validator-results/` (override with `--results-dir`). File reporters write to that directory:

- `json` — `results.json` with model, timestamp, and all verdicts
- `junit` — `results.xml` with JUnit XML test results
- `markdown` — `summary.md` with a results table, plus per-skill directories with per-scenario judge reports

## CI integration

The same CLI works in CI — use `--require-evals` to enforce eval coverage and `--verdict-warn-only` to treat verdict failures as warnings while still failing on execution errors:

```yaml
name: Validate Skill Value
on:
  pull_request:
    paths: ['**/SKILL.md', '**/tests/eval.yaml']
jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet run --project eng/skill-validator/src/SkillValidator -- --require-evals --verdict-warn-only .
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### Consolidating results across matrix jobs

When evaluating multiple components in parallel CI matrix jobs, use the `consolidate` subcommand to merge individual `results.json` files into a single markdown summary:

```bash
dotnet run --project src/SkillValidator -- consolidate --output summary.md results1.json results2.json

# Or use find/glob to discover files
dotnet run --project src/SkillValidator -- consolidate --output summary.md $(find ./all-results/ -name results.json)
```

| Flag | Description |
|------|-------------|
| `<files...>` | Paths to `results.json` files to merge |
| `--output <path>` | Output file path for the consolidated markdown |
