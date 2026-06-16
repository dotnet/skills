# Compare mode

Run telemetry + quality for two or more model assignments and emit a
side-by-side delta.

## `Compare/matrix.json`

```json
[
  {
    "name": "baseline",
    "model_assignments": {
      "router":  "gpt-4o-mini",
      "planner": "gpt-4o",
      "worker":  "gpt-4o-mini"
    }
  },
  {
    "name": "candidate",
    "model_assignments": {
      "router":  "gpt-4o-mini",
      "planner": "o4-mini",
      "worker":  "gpt-4o-mini"
    }
  }
]
```

## Runner behaviour

For each entry in the matrix:

1. Apply the `model_assignments` in-process (override the
   per-agent `IChatClient` registrations; do NOT modify
   `appsettings.json`).
2. Run telemetry mode against `Telemetry/inputs.json`.
3. Run quality mode against `Quality/golden.json`.
4. Capture both reports labeled by `name`.

Then diff:

- Latency: per-agent delta (ms) and aggregate delta.
- Tokens: input/output deltas per agent.
- Cost: aggregate delta (USD).
- Quality: pass-rate delta and per-input score delta.

## Report — `compare.md`

```markdown
# Compare — {{ utc_timestamp }}

Variants: {{ name_list }}

## Latency (avg ms per agent)

| Agent   | baseline | candidate | Δ      |
|---------|----------|-----------|--------|
| router  |   340    |    342    |   +2   |
| planner |  1240    |    980    |  -260  |
| worker  |   410    |    405    |   -5   |

## Token cost (USD per 1K turns, projected)

| Variant   | Cost   | Δ        |
|-----------|--------|----------|
| baseline  | $5.34  |   —      |
| candidate | $4.18  |  -$1.16  |

## Quality (pass rate)

| Variant   | Pass | Δ     |
|-----------|------|-------|
| baseline  |  92% |  —    |
| candidate |  90% |  -2%  |

## Recommendation

candidate ⟶ -22% cost, -260ms planner latency, -2pp quality.
If quality bar is "no regression", reject. If quality bar is
"≥ 88%", accept.
```

## Constraints

- Compare mode never edits `appsettings.json`.
- Compare mode never makes a recommendation by itself; it states the
  trade in plain terms and leaves the decision to the user.
- For ≥ 3 variants, the table grows columns; the recommendation row
  picks the variant with the best cost-quality frontier (Pareto).
