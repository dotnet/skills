# Fast evaluation profiles

Large skill-validator sweeps spend almost all of their wall-clock time in two places:
the **agent runs** (the baseline arm and the skill-enhanced arm investigating each
scenario) and the **serial judge tail** at the end of the run. This document describes
three orchestration levers — all built on flags and subcommands that already exist in
`evaluate` — plus a runner script that applies them. None of these change the
validator's default behaviour; they are opt-in orchestration on top of it.

The runner lives at [`eng/skill-validator/scripts/Invoke-FastEval.ps1`](../../scripts/Invoke-FastEval.ps1)
(PowerShell) and [`invoke-fast-eval.sh`](../../scripts/invoke-fast-eval.sh) (bash).

---

## Lever #4 — Split execute / judge

**Problem.** By default `evaluate` runs the agents and then judges each scenario in the
same invocation. The judge step holds a shared mutex and runs after the agents finish,
so it sits on the critical path as a serial tail.

**Fix.** Split the work into two phases so agents run flat-out first and judging fans out
afterward:

```
evaluate <skill> --no-judge --keep-sessions --results-dir <dir>   # persist sessions.db, no judge calls
evaluate rejudge <dir>/<timestamp>                                # judge the persisted sessions
evaluate consolidate <dir>/<timestamp>/results.json --output x.md # merge shards
```

`--no-judge` runs the requested agent arms, persists `sessions.db` (including each run's
baseline key for pairing), and makes **no** judge calls. `rejudge` re-judges the saved
baseline + treatment runs without re-running any agents. Because each skill is written to
its own results directory ("shard"), the execute phase and the rejudge phase can each run
many shards in parallel, and the judge mutex is confined to the rejudge phase.

| Step | Command | Flags it maps to |
| --- | --- | --- |
| Execute | `evaluate … --no-judge --keep-sessions --results-dir` | `--no-judge`, `--keep-sessions`, `--results-dir`, `--runs`, `--model` |
| Judge | `evaluate rejudge <ts-dir>` | `--judge-model`, `--judge-timeout`, `--judge-mode` |
| Merge | `evaluate consolidate <results.json…> --output` | `--output` |

> `--no-judge` is **mutually exclusive** with `--baseline-out` / `--baseline-from`
> (see lever #2). Pick one profile per sweep.

**Runner:** `-Mode Split` (default).

---

## Lever #5 — Fast first pass

**Problem.** The default judge configuration is tuned for maximum fidelity: overfitting
analysis is on, and the judge model can be a large, slow one. For a first-pass triage of
many skills that is more than you need.

**Fix.** A cheaper judging profile for the first pass, escalating only the borderline
skills to a full run:

- **`--no-overfitting-check`** — skip the LLM overfitting analysis (inline-judged paths).
- **`--judge-model <faster-model>`** — judge with a cheaper/faster model (e.g.
  `claude-haiku-4.5`).
- **`--judge-timeout <shorter>`** — cap slow judge calls sooner (e.g. `120`s vs `300`s).

> The validator's judge modes are `pairwise`, `independent`, and `both` — there is no
> "single" mode. `pairwise` is already a single side-by-side comparison per criterion
> (run twice with swapped order for bias control); `independent` scores each arm on its
> own. The fast first pass keeps the default `pairwise` mode and gets its savings from a
> cheaper judge model + shorter timeout, and (inline paths) skipping overfitting.

**Runner:** add `-Fast`. It substitutes `-FastJudgeModel` / `-FastJudgeTimeout` for the
full values, and in `BaselineReuse` mode also passes `--no-overfitting-check`. (In `Split`
mode overfitting analysis is not part of `rejudge`, so only the judge model/timeout apply.)

### When to escalate to a full run

Re-run a skill with the full profile (overfitting on, a full/cross-family judge model,
default timeout) when the fast pass leaves it **borderline or suspicious**:

- overall improvement score within ~±0.05 of `--min-improvement`;
- a wide confidence interval that straddles the pass threshold;
- a large gap between the fast judge and your expectation, or a suspected reward-hacky
  rubric win that only overfitting analysis would catch;
- any skill you intend to actually ship.

Because sessions are persisted (`--keep-sessions`), escalation is just another `rejudge`
over the **same** `sessions.db` with the full judge model — no agent re-runs:

```
evaluate rejudge <ts-dir> --judge-model <full-model> --judge-timeout 300
```

---

## Lever #2 — Shared baseline reuse

**Problem.** Every evaluation re-runs the baseline arm (agent with no skill loaded) for
each skill, even though that arm is skill-independent. Across N skills sharing the same
scenarios that is up to ~1/3 of all agent runs spent recomputing the same control group —
and it injects run-to-run variance into every comparison.

**Fix.** Compute the baseline once and reuse it:

```
evaluate <skill-a> --baseline-out baseline.json …   # persist the averaged baseline arm
evaluate <skill-b> --baseline-from baseline.json …  # skip the baseline arm, reuse it
```

`--baseline-from` skips the baseline arm entirely; the cached baseline feeds assertions,
judging, and metric deltas. The file records the agent model, judge model, and a
per-scenario SHA over the prompt + setup inputs + evaluation criteria, so a stale or
mismatched baseline fails fast rather than being silently applied. This path **judges
inline** (baseline reuse cannot combine with `--no-judge`).

**Runner:** `-Mode BaselineReuse`. The first skill produces the baseline; the rest reuse
it in parallel.

---

## Choosing a profile

| Situation | Profile |
| --- | --- |
| Many skills, want the judge tail off the critical path, judge later / in CI matrix | `-Mode Split` |
| Many skills over the **same** scenarios, want to stop recomputing the baseline arm | `-Mode BaselineReuse` |
| First-pass triage; only borderline skills need full fidelity | add `-Fast` to either |
| Shipping / borderline skill | full run (no `-Fast`), or `rejudge` the kept sessions with a full judge model |

`-Mode Split` and `-Mode BaselineReuse` optimise different phases (judging vs. the
baseline agent arm) and are mutually exclusive in a single invocation because the
validator forbids `--no-judge` together with the baseline-reuse flags. For a sweep that
wants both, run `BaselineReuse` (which already removes the baseline arm and judges inline)
with `-Fast`, then escalate borderline skills.

## Expected savings (rules of thumb)

Actual numbers depend on scenario cost, `--runs`, and model choice. Directionally:

- **#4 Split** — removes the serial judge tail from the critical path and lets execute +
  rejudge each run at full parallelism; the judge mutex is confined to the rejudge phase.
  Biggest win when judging is a large fraction of wall time or when sharding across a CI
  matrix.
- **#5 Fast** — cuts judge cost per scenario (cheaper model, shorter timeout, and inline
  overfitting analysis skipped). Overfitting analysis is a whole extra judged pass, so
  skipping it on the first pass is a substantial saving on inline runs.
- **#2 Baseline reuse** — removes the baseline arm for every skill after the first:
  up to ~1/3 fewer agent runs when baseline/isolated/plugin arms are all in play, and it
  removes baseline variance from the comparison.

## Verifying / investigating

Both scripts emit each underlying `skill-validator` command as they go, and the split flow
leaves per-shard `results.json` and `sessions.db` on disk for inspection or re-judging.
For failure triage of the produced results, see
[`InvestigatingResults.md`](./InvestigatingResults.md).
