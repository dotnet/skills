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

- Node.js >= 20
- Authenticated with GitHub via `gh auth login` (the SDK picks up your credentials automatically)

## Install

```bash
npm install
npm run build
npm link        # makes `skill-validator` available globally
```

## Usage

```bash
# Validate all skills in a directory
skill-validator ./path/to/skills/

# Validate a single skill
skill-validator ./path/to/my-skill/

# Verbose output with per-scenario breakdowns
skill-validator --verbose ./skills/

# Custom model and threshold
skill-validator --model claude-sonnet-4.5 --min-improvement 0.2 ./skills/

# Use a different model for judging vs agent runs
skill-validator --model gpt-5.3-codex --judge-model claude-opus-4.6-fast ./skills/

# Multiple runs for stability
skill-validator --runs 5 ./skills/

# Output as JSON or JUnit XML
skill-validator --reporter json:results.json ./skills/
skill-validator --reporter junit:results.xml ./skills/

# Strict mode (require all skills to have evals)
skill-validator --strict ./skills/

# Custom results directory
skill-validator --results-dir ./my-results ./skills/
```

## Writing eval files

Each skill can include a `tests/eval.yaml`:

```yaml
scenarios:
  - name: "Descriptive name of the scenario"
    prompt: "The prompt to send to the agent"
    setup:
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
| `--confidence-level <n>` | `0.95` | Confidence level for statistical intervals (0–1) |
| `--judge-timeout <n>` | `300` | Judge LLM timeout in seconds |
| `--require-completion` | `true` | Fail if skill regresses task completion |
| `--require-evals` | `false` | Fail if skill has no tests/eval.yaml |
| `--strict` | `false` | Enable --require-evals and strict checking |
| `--verbose` | `false` | Show tool calls and agent events during runs |
| `--reporter <spec>` | `console` | Output format: `console`, `json:path`, `junit:path` |
| `--results-dir <path>` | `.skill-validator-results` | Directory for saved run results |
| `--no-save-results` | | Disable saving run results to disk |

Models are validated on startup — invalid model names fail fast with a list of available models.

## Output

Results are displayed in the console with color-coded scores and metric deltas. Run results are also auto-saved to `.skill-validator-results/run-{timestamp}/` containing:

- `results.json` — full results with model, timestamp, and all verdicts
- Per-skill directories with `verdict.json` and per-scenario markdown files

## CI integration

The same CLI works in CI — `--strict` makes it fail on any issue:

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
      - uses: actions/setup-node@v4
      - run: npx skill-validator --strict --require-evals .
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

## Development

```bash
npm test          # Run unit tests
npm run test:watch # Watch mode
npm run lint      # Type check
npm run build     # Compile TypeScript
```
