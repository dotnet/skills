---
name: create-skill-evaluation
description: >
  Write eval.yaml files that measure whether a SKILL.md improves agent output.
  USE when creating or improving tests/<plugin>/<skill>/eval.yaml.
  DO NOT USE for writing SKILL.md content itself or for running the skill-validator CLI.
---

# Create Skill Evaluation

Produce an `eval.yaml` that the skill-validator can run to prove a skill adds measurable value.
The eval file lives at `tests/<plugin>/<skill-name>/eval.yaml`.

## Inputs

Before writing an eval, you need:

1. The **SKILL.md** being evaluated — read it to catalog every teaching
2. The **plugin domain** — determines setup approach (project scaffolding, fixtures, inline files)
3. The **CONTRIBUTING.md** and **eng/skill-validator/README.md** — reference for assertion types, setup options, and CLI flags

## Eval design principles

### Scenarios describe goals, not techniques

The prompt must read like a real user request: "I need a component that does X."
It must never mention concepts the skill teaches. If the skill teaches retry policies
with Polly, the prompt says "make the HTTP calls resilient" — not "add a Polly retry policy."
When the prompt contains the skill's teachings, the baseline gets the same guidance
and the eval cannot measure the skill's contribution.

### Scenario domains must not overlap with SKILL.md examples

If the skill teaches caching with a "product catalog" example, do not create an
eval scenario about product catalogs. The agent may memorize and replay the example
rather than generalize. Choose unrelated domains — weather data, appointment scheduling,
audit logging — so the eval measures whether the agent internalized the pattern.

### 5 scenarios is the sweet spot

Enough diversity to avoid overfitting to one problem shape, few enough to finish
in reasonable time. Each scenario should exercise a different subset of the skill's
teachings so no single pattern carries the entire score.

### Baselines should be achievable but imperfect

If the baseline already scores 5/5 on a scenario, the skill cannot show improvement.
Pick problems where an unskilled agent produces working but non-idiomatic code — the
kind of gap a skill is designed to close.

## eval.yaml structure

### Setup

Choose the setup approach based on what the scenario needs:

| Approach | When to use | Example |
|----------|-------------|---------|
| `commands:` | Agent needs a scaffolded project | `dotnet new webapi --no-https` |
| `copy_test_files: true` | Fixtures live alongside eval.yaml | Legacy migration scenarios |
| `files:` (inline) | Specific file contents required | Broken code that needs fixing |
| No setup | Agent creates everything from scratch | Pure authoring tasks |

Use YAML anchors when all scenarios share the same setup:

```yaml
scenarios:
  - name: "First scenario"
    setup: &api_project
      commands:
        - "dotnet new webapi --no-https"
    # ...

  - name: "Second scenario"
    setup: *api_project
    # ...
```

### Prompts

Write prompts as a user would — describe the goal with bulleted requirements:

```yaml
prompt: |
  I need a `RetryHandler` for my ASP.NET Core app.

  Requirements:
  - Wraps HttpClient calls with automatic retry on transient failures
  - Configurable max retry count and base delay via DI options
  - Uses exponential backoff with jitter
  - Logs each retry attempt with the attempt number and delay
  - After exhausting retries, throws the original exception
  - Registered as a DelegatingHandler in the DI container
```

Do not include architecture decisions, pattern names, or implementation hints
that come from the skill. The prompt describes *what*, the skill provides *how*.

### Assertions

Use 3–4 assertions per scenario as structural gates. Assertions verify necessary
conditions — the rubric handles quality.

```yaml
assertions:
  - type: "output_contains"
    value: "DelegatingHandler"
  - type: "output_contains"
    value: "ILogger"
  - type: "output_matches"
    pattern: "(AddHttpMessageHandler|AddTransientHttpErrorPolicy)"
```

Available assertion types:

| Type | Purpose |
|------|---------|
| `output_contains` | Key type/method name appears in agent output |
| `output_not_contains` | Banned pattern is absent (e.g., hardcoded secrets) |
| `output_matches` | Regex for alternative valid patterns |
| `file_exists` | Expected file was created |
| `file_contains` | File has specific content |
| `exit_success` | Agent produced non-empty output |

### Rubric items

Write 10–13 items per scenario. Each item is one observable behavior scored 1–5
by the LLM judge.

**Format**: "Does [correct thing] — not [common mistake]"

```yaml
rubric:
  - "Implements retry as a DelegatingHandler — not as inline try/catch in every call site"
  - "Uses exponential backoff with jitter — not fixed-delay retry"
  - "Registers via AddHttpMessageHandler in DI — not manually wrapping HttpClient"
  - "Logs each retry with attempt number and computed delay — not silent retries"
  - "Configures retry options through IOptions<T> pattern — not hardcoded constants"
  - "Only retries on transient HTTP errors (408, 429, 5xx, network failures) — not on 4xx client errors"
  - "Throws the original exception after exhausting retries — not a wrapped AggregateException"
  - "Disposes no unmanaged resources and does not implement IDisposable unnecessarily"
```

Rules:
- One behavior per item — the judge must be able to score it independently
- Name specific APIs and methods — "uses `AddHttpMessageHandler`" not "registers correctly"
- Include anti-pattern phrasing — tells the judge what penalties to apply
- Don't rubric things the baseline already does perfectly — wastes discriminative power
- Add items for patterns the skill explicitly warns against — these are frequently missed

### Timeout

| Scenario type | Timeout |
|---------------|---------|
| Analysis only (no code generation) | 120s |
| Code generation, simple scaffolding | 180–300s |
| Code generation with project setup + multiple files | 300–600s |

If rubric count exceeds 10 items per scenario, use `--judge-timeout 600` when running
the validator.

## Iteration workflow

### 1. Fast feedback (1-run)

```bash
dotnet run --no-launch-profile --project eng/skill-validator/src/SkillValidator.csproj \
  -- <skill-path> --tests-dir tests/<plugin> --runs 1 --verbose \
  --reporter json --reporter markdown --no-overfitting-check \
  --keep-work-dirs --work-dir artifacts/work-dirs
```

### 2. Check baseline scores

For each rubric item, look at the baseline score. If baseline scores 5/5 on an
item, that item adds no signal — remove or replace it with something the baseline
misses.

### 3. Check for equal failures

If both baseline and skilled variants fail a rubric item equally, either:
- The skill doesn't teach that pattern (skill gap — fix the SKILL.md)
- The rubric is unfair (asks for something unreasonable — rewrite the item)

### 4. Compare structural diversity across variants

After a 1-run eval, read the baseline and skilled output for each scenario.
If the generated code has the same file structure, class hierarchy, and API
surface across both variants, the scenario is too constrained or the skill
is not changing how the agent approaches the problem. Good scenarios show
structural differences: the skilled variant creates a wrapper class the baseline
does not, uses a different disposal pattern, or organizes files differently.
If 3+ scenarios produce near-identical baseline vs skilled code, diversify the
scenario domains or loosen the prompts.

### 5. Read judge reasoning

The results JSON contains per-rubric `reasoning` from the judge. Read it to
understand why specific items scored low — the judge often reveals whether the
agent missed a pattern or the rubric was ambiguous.

### 6. Final validation (5-run)

```bash
dotnet run --no-launch-profile --project eng/skill-validator/src/SkillValidator.csproj \
  -- <skill-path> --tests-dir tests/<plugin> --runs 5 --verbose \
  --reporter json --reporter markdown --no-overfitting-check \
  --judge-timeout 600
```

The eval passes when overall score is ≥ +10% and all scenarios show improvement.

## Common mistakes

| Mistake | Why it hurts |
|---------|-------------|
| Prompt leaks skill teachings | Mentioning "use the Options pattern" or "create a handler class" in the prompt gives the baseline the same guidance the skill provides — eval cannot measure improvement |
| All scenarios exercise the same 2–3 teachings | The eval appears to pass but only validates a fraction of the skill |
| Scenario domains overlap with SKILL.md examples | Agent replays memorized examples instead of generalizing |
| Baseline and skilled outputs are structurally identical | Scenarios are too narrow or prompts too prescriptive — the skill has no room to change the agent's approach |
| Rubric tests vocabulary instead of outcomes | "Uses the word 'resilience'" vs "Retries on transient failures" — the first is overfitting, the second measures behavior |
| Compound rubric items | "Does A and B and C" — the judge cannot score partially; split into separate items |
| Assertions check things unrelated to the skill | A `file_exists` check for `README.md` when the skill teaches error handling — noise |
| Timeout too short | Agent gets killed mid-generation; scored as failure when it was simply slow |
| Missing anti-pattern rubrics | The skill warns "don't do X" but the rubric never penalizes X — the eval misses a key signal |

## Checklist

Before submitting the eval for review:

- [ ] 5 scenarios with diverse problem domains
- [ ] No domain overlap between eval scenarios and SKILL.md examples
- [ ] Prompts describe goals — no skill concepts or pattern names mentioned
- [ ] 3–4 assertions per scenario (structural gates)
- [ ] 10–13 rubric items per scenario (quality bar)
- [ ] Each rubric item is a single observable behavior
- [ ] Anti-pattern phrasing on items that catch common mistakes
- [ ] Timeout appropriate for setup complexity
- [ ] YAML anchors for shared setup
- [ ] 1-run eval: baseline vs skilled outputs are structurally different
- [ ] 1-run eval: no rubric item scores 5/5 on baseline
- [ ] 5-run eval passes with ≥ +10% overall
