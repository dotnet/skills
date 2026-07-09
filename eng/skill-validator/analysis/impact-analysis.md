# Multi-model skill validation — Impact Analysis

**Scope:** Run skill evals against latest Opus / GPT / Sonnet, with the judge never equal to the
agent (default judge = latest Opus; the Opus leg is judged cross-family by GPT). Adopt missing Vally
patterns. Preserve `dotnet/skills` + `dotnet/skills-data` history. **Nothing committed or pushed.**

---

## 1. What changed

| File | Change |
|------|--------|
| `eng/eval-models.json` **(new)** | Single source of truth: `latest` ids, `gatingModel`, `matrix`, `defaultJudge`, `judgeOverrides`. |
| `eng/resolve-judge.mjs` **(new)** | Resolver: `judge/models/matrix/selftest`. Enforces judge≠agent; drift guard vs `latest`. |
| `eng/vally-adapter/publish-vally-data.mjs` **(new)** | Model-keyed flatten + merge + retention for Vally history. |
| `.github/workflows/evaluation.yml` | Gating (PR) = single model + resolved judge; nightly = 3-model matrix; artifacts `…@<model>`; publish jobs model-aware. |
| `.github/workflows/vally-evaluation.yml` | Version-pinned; 3-model matrix + per-model judge; `normalize` job (dedupe shards→plugins); publishes to `dashboard-vally-data`. |
| `eng/dashboard/build-replay-sessions.ps1` | Session filename/id/tags keyed by model; strips `@<model>` artifact suffix. |
| `eng/dashboard/purge-replay-sessions.ps1` | Orphan parser updated for `<scenario>--<role>--<model>--run<N>`. |
| `eng/dashboard/dashboard.js` | Per-plugin model selector; filters entries by model (no cross-model averaging). |
| `eng/vally-adapter/adapt.mjs`, `run-vally-evals.sh` | Judge defaults aligned to resolver (judge≠agent). |

**Core fix delivered incidentally:** both workflows previously ran `MODEL == JUDGE_MODEL`
(`claude-opus-4.6` judging itself). Every leg now has judge ≠ agent.

---

## 2. What breaks / what to watch (ranked)

### 🟢 R1 — Model IDs are UNVERIFIED → now guarded by a fail-fast preflight — *partially mitigated*
`claude-opus-4.8`, `claude-sonnet-5`, `gpt-5.5` still cannot be checked against the CI Copilot token's
model list from here (no token locally). **Gating depends on TWO of them** (agent `claude-opus-4.8` +
judge `gpt-5.5`); if either is unavailable, required PR evaluation would fail.
- *Evidence:* previous gating used a single known id (`claude-opus-4.6`); the new ids exist only in
  `eng/eval-models.json`.
- *Mitigation (IMPLEMENTED):* all ids are centralized (one-line fix in `eval-models.json`) **and** a
  dedicated **ListModels preflight now runs once, before the eval matrix fans out**:
  - Gating/nightly workflow (`evaluation.yml`): new `preflight-models` job runs
    `skill-validator list-models --require <agent∪judge ids>` — the *same* `ListModelsAsync()` call the
    eval already makes — using the resolver's new `required` set. `evaluate` `needs` it and only runs on
    its success, so a bad/renamed id fails in seconds with the missing ids named, instead of after every
    plugin×model leg hits the same "Invalid model" error mid-run.
  - Vally workflow (`vally-evaluation.yml`): new `preflight-models` job runs `eng/preflight-models.mjs`,
    which uses the Copilot SDK's `listModels()` (vally has no `models` subcommand) — the node analogue of
    the .NET check — and gates `vally-evaluate`.
  - Resolver gained a `required [--gating|--nightly]` subcommand emitting the sorted, de-duplicated union
    of agent models and their resolved judges (the exact set the token must satisfy).
  - *Residual:* the live availability of the three ids is still only confirmed at first CI run; the
    preflight makes that failure fast, cheap, and unambiguous rather than eliminating it.

### 🟠 R2 — Gating blast radius widened — *proven*
Old gate = 1 model. New gate = 1 agent **+** 1 distinct judge. Two model dependencies instead of one.
- *Evidence:* `resolve-judge.mjs matrix --gating` → `[{model:claude-opus-4.8, judge:gpt-5.5}]`.
- *Tradeoff, by design* (you asked judge≠agent even for gating). Net correctness win (no self-judge),
  slightly larger dependency surface.

### 🟠 R3 — Nightly cost/throughput ×3 — *proven*
Nightly skill-validator and Vally now run 3 agent legs, each with its own judge invocation.
- *Evidence:* `matrix --nightly` → 3 pairs; matrix is `entry × pair`.
- *Mitigation already added:* Vally `normalize` job collapses `<plugin>--shard-*` legs to one entry
  per plugin, so Vally cost is `plugins × models` (not `shards × models`). This also fixes a
  **pre-existing** duplication (Vally always ignored shards and ran whole plugins).

### 🟡 R4 — Legacy session replays drop off immediately — *proven*
Old files are `<scenario>--<role>--run<N>.jsonl` (3 fields); new are 4. The purge orphan-parser now
requires ≥4 fields, so pre-existing (all `claude-opus-4.6`) orphan replays are skipped, not
mis-parsed.
- *Why not back-parse:* a 4-part name is ambiguous between legacy `scenario--with--dashes` and new
  `scenario--role--model--runN`, so safe auto-migration isn't possible.
- *Impact bounded:* session retention is **7 days** and this is non-gating shadow data. Old replays
  age out within a week. *Recommend* a one-time manual purge of the session branch at rollout to
  avoid a mixed week.

### 🟡 R5 — Vally history has no dashboard consumer yet — *proven*
Vally now publishes to a **new** `dashboard-vally-data` branch on `dotnet/skills-data`, but
`deploy-dashboard` doesn't fetch/render it.
- *By design:* Vally's trajectory/schema differs from skill-validator's AGENTVIZ replays, so mixing
  them into existing branches would corrupt the current dashboard. Rendering Vally history is
  **follow-up work** (the data is captured now so history starts accumulating immediately).

### 🟢 R6 — Dashboard default model = most-recent leg (Sonnet) — *proven, cosmetic*
The per-plugin selector defaults to the last-appended model (matrix order ends on Sonnet). Not a
correctness issue; single-model history renders identically (selector hidden). Change the default to
the gating model later if preferred.

---

## 3. History carry-forward (dotnet/skills + dotnet/skills-data)

- **Eval + token history (`dashboard-eval-data` / `dashboard-token-data`):** *preserved.*
  `generate-benchmark-data.ps1` already tags every datapoint with its model and **appends** (no
  dedupe by model). Existing `claude-opus-4.6` points stay valid; new runs append model-tagged
  points. The dashboard's new model filter renders each model (incl. the historical `4.6`) as its own
  series — **no mixing, no loss.** Retention (14 days) is unaffected (per-datapoint, model-agnostic).
- **Session replays (`dashboard-session-data` on skills-data):** model added to filename/id/tags so
  the 3 legs no longer overwrite each other via `Copy-Item -Force`. Legacy 3-field files age out (R4).
- **Vally history:** brand-new `dashboard-vally-data` branch, model-keyed from day one (R5).

---

## 4. Evidence for correctness claims

- **Judge≠agent (proven):** `selftest` →
  `opus-4.8→gpt-5.5`, `gpt-5.5→opus-4.8`, `sonnet-5→opus-4.8`. No self-judge; Opus leg cross-family.
- **Artifact naming collision-safe (proven):** `@` separator; plugin/skill/shard names match
  `^[a-zA-Z0-9._-]+$` (no `@`); publish jobs split on last `@`. Synthetic test: `dotnet` vs
  `dotnet-maui` never cross-match.
- **Session filename round-trip (proven):** build→parse verified incl. a scenario containing `--`.
- **Shard dedupe (proven):** 2×`dotnet--shard-*` + `dotnet-maui` → `[dotnet, dotnet-maui]`.
- **Drift guard (proven):** bumping `latest.opus` without updating derived fields → `selftest` exits 1.
- **Preflight `required` set (proven):** `resolve-judge.mjs required --gating` → `["claude-opus-4.8","gpt-5.5"]`;
  `--nightly` → `["claude-opus-4.8","claude-sonnet-5","gpt-5.5"]`. `skill-validator list-models` builds
  clean (0 warnings) and the verb + `--require` help render. `preflight-models.mjs parseRequired` unit
  tests pass (JSON-array, csv, space, dedupe, empty).
- **Validation:** actionlint = 0 (both workflows), pwsh parse OK (both scripts), `node --check` OK
  (all js/mjs), `.NET` build OK, JSON OK.

*Unverifiable here:* live model availability (R1 — the preflight makes this a fast, explicit CI failure
rather than a silent mid-matrix one) and dashboard JS rendering against real data branches (needs a real run).

---

## 5. Recommended rollout order

1. **Verify the 3 model ids** against the CI token. This is now enforced automatically: the
   `preflight-models` job (both workflows) fails fast and names any missing id before the matrix runs.
   If it goes red on first run, fix `eval-models.json` (single source of truth).
2. Land nightly-multi-model + Vally-multi-model first (non-gating); confirm dashboard renders the new
   per-model series.
3. Flip gating to `opus-4.8` + `gpt-5.5` **only after** step 1.
4. One-time purge of the session-data branch to clear legacy 3-field replays (optional; else they age
   out in 7 days).
5. Follow-up: teach `deploy-dashboard` to render `dashboard-vally-data`.

---

## 6. Empirical grading impact — minimal real run (numbers)

*Method:* ran the agent arms ONCE per model (`runs=1`) on a toy `sample-skill` greeting fixture
(2 scenarios), persisted the sessions, then **re-judged the identical persisted outputs** with different
judge models (`evaluate rejudge`). Agent outputs are held constant; only the judge varies. This
cleanly isolates the two things the change actually moves: **who executes** and **who judges**. Judge
mode = Pairwise (production default); pass threshold = 10%.

### Factor A — judge swap on IDENTICAL agent outputs
| Agent | SELF-judge (old style) | CROSS-judge (new) | Δ (cross − self) |
|-------|-----------------------:|------------------:|-----------------:|
| claude-opus-4.8 | −14.6% (judge opus-4.8) | −25.1% (judge gpt-5.5) | **−10.5 pp** |
| gpt-5.5         | −24.7% (judge gpt-5.5)  | −8.7% (judge opus-4.8) | **+16.0 pp** |
| claude-sonnet-5 | −12.9% (judge sonnet-5) | −18.9% (judge opus-4.8)| **−6.0 pp** |

Swapping ONLY the judge moved the aggregate score by **6–16 pp on byte-identical outputs**.

### Factor B — agent-model effect under the NEW production judge rule
| Agent (production cross-judge) | Score |
|--------------------------------|------:|
| claude-opus-4.8 (judge gpt-5.5)  | −25.1% |
| gpt-5.5 (judge opus-4.8)         | −8.7% |
| claude-sonnet-5 (judge opus-4.8) | −18.9% |

Spread across agent models = **16.4 pp** on the same fixture. All three FAIL (< 10%), as expected: the
toy greeting "skill" adds ~no value over a baseline that already greets (and sometimes adds an unrelated
tool call), so a negative marginal score is correct behaviour, not a defect.

### What this proves / doesn't
- **Proven:** the grading *formula* is unchanged (same 70%-judge weighting). The change moves (a) which
  model executes and (b) who judges. On fixed outputs, changing only the judge moves aggregate scores by
  double-digit pp — so judge choice has high leverage (consistent with the 70% judge weight).
- **Retracted (per cross-family rubber-duck, gpt-5.5):** an earlier "no self-preference bias" claim. The
  design does not test classic self-preference (baseline and with-skill share the agent model), and 3
  points can't rule it out. What we *observed* is judge strictness/leniency variance, e.g. gpt-5.5 graded
  its own agent arm harshly (−24.7%) while opus-4.8 was the least harsh judge in these crossings.
- **Caveats:** `runs=1` × 2 scenarios on a toy fixture ⇒ **directional, not significant** (validator itself
  recommends ≥5 runs; CI = 0 here). Each rejudge ran once, so the deltas blend judge-model difference with
  judge invocation variance. No pass/fail *flip* is demonstrated (all arms fail), but since observed judge
  deltas exceed the 10 pp threshold, a near-threshold skill could plausibly flip on judge choice alone.

### Consequence for dotnet/skills-data history
Old history was produced under **opus-4.6 self-judge**. New scores come from cross-family judges on
newer agents. These are **not directly comparable** — a fixed skill can move ≥10 pp from the judge change
alone. History must be **segmented by `(agent, judge, judge-mode)`**, never pooled. This is exactly why
the dashboard schema was re-keyed by model (§ layers 4–5) rather than overwriting a single series.

*Model availability (real, this environment):* preflight saw all 3 required ids present —
node SDK enumerated 16 models, `skill-validator list-models` enumerated 17; both include
claude-opus-4.8, gpt-5.5, claude-sonnet-5 (MISSING = none, exit 0).

---

## 7. Log-privacy fix — model roster is never dumped to CI logs

**Concern:** the preflight printed the *full* set of available model ids to CI logs — on success
("Available models (N): …") and again inside the failure branch ("Available models: …"). Anyone
reading a public Actions log would see the entire roster the token can reach.

**Fix (both preflight implementations, kept in sync):**
- `eng/skill-validator/src/Evaluate/ListModelsCommand.cs` (.NET) and `eng/preflight-models.mjs` (node).
- When `--require` is set (the ONLY path CI uses), the tools no longer print the roster. They emit:
  - success → `All N required model(s) available: <required ids>` (the required ids come from the
    in-repo, already-public `eval-models.json`, so echoing them leaks nothing);
  - failure → `::error:: Required model id(s) unavailable: <missing>` + `Missing model(s): <missing>`
    + `Available model count: N (list withheld).` — names only the missing required ids and a count.
- The full roster is emitted only on an explicit *local* invocation: a bare `list-models` with no
  `--require`, or `--json`. Neither is used by CI.

**Verified live (this environment):**
- `list-models --require claude-opus-4.8,gpt-5.5,claude-sonnet-5` → `All 3 required model(s) available: …`
  (no roster).
- `list-models --require claude-opus-4.8,does-not-exist-9` → `::error::…: does-not-exist-9` /
  `Missing model(s): does-not-exist-9` / `Available model count: 17 (list withheld).` (exit 1, no roster).
- `.NET` rebuild = 0 warnings / 0 errors; `node --check` clean; `global.json` left untouched
  (transient SDK shim used for the build was restored).

**Docs updated:** `InvestigatingResults.md` no longer tells investigators the log "lists the available
models" — it now says only the missing id(s) + a withheld-count are shown.

---

## 8. Real-skill before/after — judge swap on identical outputs (bounded sample)

*Method:* generated agent outputs ONCE on `claude-opus-4.8` for 3 real repo skills (12 scenarios,
`runs=1`), persisted them, then re-judged the identical outputs with SELF judge (`claude-opus-4.8`,
old-style) vs CROSS judge (`gpt-5.5`, new gating pair). Agent output held constant; only the judge
varies. Pairwise mode; 10% pass line.

### Skill-level (n=3)
| Skill | SELF (opus-4.8) | CROSS (gpt-5.5) | Δpp | self | cross |
|-------|----------------:|----------------:|----:|------|-------|
| setup-local-sdk              | +38.6% | +38.4% | −0.2 | PASS | PASS |
| msbuild-antipatterns         | −2.3%  | −5.3%  | −3.0 | fail | fail |
| migrate-nullable-references  | −2.4%  | −6.4%  | −4.0 | fail | fail |

Skill-aggregate deltas are small (−0.2…−4.0 pp) and **no verdict flipped**. All three moved down
(mean −2.4 pp) — but 3/3 is weak evidence (sign-test p≈0.125), so read this as "scored slightly lower
in this sample," NOT "GPT is uniformly stricter."

### Scenario-level (n=12, clustered by skill — NOT independent)
Mean Δ = **−2.1 pp**, but the per-scenario spread is **much wider: −24.0 pp … +18.8 pp**. 7/12
scenarios were judge-invariant (both judges gave the identical pairwise verdict → identical score);
among the 5 that moved, swings were large in **both** directions (e.g. "Create team install scripts"
+41.1→+17.1 = −24 pp; "Set up local SDK with MAUI" −20.2→−1.4 = +18.8 pp; "Enable NRT in ASP.NET Core"
+5.5→−6.5 = −12 pp, crossing zero). Skill-level stability comes from (a) many scenarios agreeing exactly
and (b) large per-scenario swings partially cancelling under aggregation.

### Contrast with the toy fixture (§6)
The toy no-op greeting skill swung 6–16 pp under the same judge swap; these real skills moved less at the
skill level. This *suggests* — but does not establish — that clearer real-skill signal reduces judge
sensitivity; a near-zero-value skill sits in the noisy band where judge "personality" dominates.

### What this shows / does not show
**Shows:** holding opus-4.8 outputs fixed, switching judge opus→gpt changed real-skill scores by
−0.2…−4.0 pp (skill-level) in this sample; no pass/fail flip among the 3; the toy was more
judge-sensitive than these real skills.
**Does NOT show:** repo-wide judge robustness; a validated risk band; that GPT is always stricter; the
effect of *also* changing the agent model (opus/gpt/sonnet — measured only on the toy, ~16 pp spread);
judge stochasticity (each rejudge ran once, so a ±2–4 pp delta may be within grading noise).

### Projection to all 98 skills (explicitly a heuristic, not a measurement)
- Grading **formula is unchanged**; verdicts for clearly-good (e.g. setup-local-sdk, +38%) and
  clearly-bad skills are stable under the judge swap.
- **At-risk = skills near the 10% line.** Per-scenario swings up to ~24 pp were observed, so a scenario
  or borderline skill within roughly the [+6%, +14%] band could flip pass↔fail purely from the judge
  change. ±(a few) pp is a *provisional inspection band from this small sample, not a bound.*
- The agent-model change adds a second, likely-larger source of movement (toy: ~16 pp across models),
  so the true at-risk band under the full change is probably **wider** than the judge-only band here.
- **History (`dotnet/skills-data`): do not pool.** Old self-judge scores and new cross-judge scores must
  be segmented by `(agent, judge, judge-mode)`; per-scenario comparisons across the config change are
  especially noisy (±double-digit pp) and should not be diffed 1:1.

*To get real numbers for all 98:* run the modified CI matrix (98 × 3 models, CI tokens/parallel runners)
— the only affordable venue for full coverage. Requires a push (not done; awaiting permission).

---

## 9. Full multi-model sweep — results (all 98 skills x 3 agent models)

**Run:** 294 jobs = 98 targets (94 skills + 4 agents) x {claude-opus-4.8, gpt-5.5, claude-sonnet-5}, cross-family judged (opus->gpt-5.5, gpt-5.5->opus-4.8, sonnet-5->opus-4.8), runs=1, verdict-warn-only.
**Outcome:** 294/294 completed, **0 failures, 0 rate-limit errors**. Wall clock ~4.5 h effective (after an overnight host-sleep wedge was recovered by killing orphans + relaunching; throughput scaled with 3 concurrent claim-locked drivers, ~9-way parallel, peaking ~128 jobs/h).

### 9.1 Per-agent-model scoreboard (proof: status/*.json, n=98 each)
| Agent model | Pass rate | Mean improvement | Median |
|---|---|---|---|
| claude-sonnet-5 | 37 / 98 (38%), mean 6.8%, median 9.3% |
| gpt-5.5 | 33 / 98 (34%), mean 5.8%, median 8.7% |
| claude-opus-4.8 | 28 / 98 (29%), mean 3.2%, median 2.6% |

### 9.2 Cross-model agreement (proof: pass/fail grouped by target, n=98 targets)
- **Pass/fail DISAGREEMENTS across the 3 models: 49 / 98 (50%)** — half of all skills flip verdict depending on which model executes them. This is the headline finding: a single-model gate was measuring the model as much as the skill.
- Unanimous PASS (all 3): 9
- Unanimous FAIL (all 3): 40

### 9.3 Which model authors the best edit per skill (proof: max score per target)
- gpt-5.5 best on 39/98; claude-sonnet-5 best on 35/98; claude-opus-4.8 best on 24/98.
- No model dominates -> **the cross-family, non-self judge matters**: the old default (Opus judging Opus) both executed and graded with the weakest-mean arm here.

### 9.4 Notable per-skill spreads (proof: scoreboard_pivot.csv)
| Skill | opus | sonnet | gpt | spread |
|---|---|---|---|---|
| dotnet-diag/microbenchmarking | 7.2 | -54.6 | 53.4 | 108.0 |
| dotnet-msbuild/build-parallelism | 21.5 | 86.8 | -6.8 | 93.6 |
| dotnet-msbuild/resolve-project-references | -20.1 | -10.9 | -88.9 | 77.9 |
| dotnet-msbuild/build-perf-baseline | -100 | -25.2 | -78.7 | 74.8 |
| dotnet-advanced/nuget-trusted-publishing | 1.6 | -37.3 | 35.0 | 72.3 |

### 9.5 Interpretation
- **Before (single-model, Opus self-judged):** one score per skill; verdict conflated skill quality with Opus behavior. 40/98 skills that unanimously fail here would have looked like "the skill's fault" but are genuinely hard for every model.
- **After (3-model, cross-family judge):** each skill gets a robustness profile. The 49 disagreement skills are exactly where a single-model gate was fragile — a skill could pass/fail purely by swapping the runner.
- **Caveat:** runs=1, so per-skill numbers carry run-to-run variance (no CIs). Directional/aggregate signal is solid; individual skill scores near 0 should be deepened at higher runs before acting.

**Artifacts:** fullrun/scoreboard_jobs.csv (294 rows), fullrun/scoreboard_pivot.csv (98 rows), fullrun/status/*.json (authoritative).

---

## 10. Scale follow-up: the remaining 93-skill `n=5` continuation is blocked

After the successful 5-skill `n=5` batch, I attempted the remaining **93 skills** under the old
routing (`opus→gpt`, `gpt→opus`, `sonnet→opus`) via 5 child batches. **None of the 5 batches
has produced a final `results.json`** at report time.

**Proven from the batch artifacts:**
- Two primary failure modes appeared:  
  **(a)** `Failed to append to JSONL file session-state\\events.jsonl: timeout while waiting for
  mutex to become available` during judge persistence/fallbacks, and  
  **(b)** repeated `contained no JSON` judge parse failures, especially on Opus-judged sweeps.
- The persistence error surfaced across **all 5** batches, not one outlier.
- At least one sweep exited **0** and still produced **no `results.json`**, so the issue is not
  just long runtime — successful artifact emission itself is unreliable.
- The batch summaries did **not** identify API 429/rate-limit as the main blocker.

**Implication:** the current validator/judge path is **not reliable enough to run the full
93-skill continuation in the same parallel shape**.

### Better ways to do the full validation next

1. **Sequential batches (recommended):** 10–20 skills per batch, but run **one batch at a time**
   and **one sweep at a time**.
2. **Agent-first / judge-later:** run the catalog with `--no-judge`, persist sessions, then
   rejudge saved sessions in tiny batches with a **non-Opus** judge first (GPT or Sonnet).
3. **Tiered runs:** use `n=3` broadly, escalate only the disagreement / near-threshold
   skills to `n=5` (skip `n=1` so no decision rests on a single draw).
4. **True serialization canary:** `--parallel-skills 1 --parallel-scenarios 1 --parallel-runs 1`
   **plus `--no-overfitting-check`**. `1/1/1` by itself is not fully serialized because the
   overfitting check still runs in parallel.
5. **Disable overfitting-check on broad sweeps:** reserve it for shortlisted skills instead of
   the whole catalog.

**Recommendation:** do **not** interpret the blocked overnight continuation as a model-quality
signal. Treat it as an **infra reliability issue**, ship the decision report from the completed
5-skill `n=5` batch plus the partial A/B and blocked-batch evidence, then rerun the full catalog
only after changing the execution shape (preferably sequential batches with agent/judge
separation).
