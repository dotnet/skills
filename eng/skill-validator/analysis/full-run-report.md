# Multi-Model Skill Validation — Full Run Report

_Generated 2026-07-07 · branch `abhitejjohn-silver-barnacle` · **nothing committed or pushed**_

Every one of the 98 skills/agents in `dotnet/skills` was executed by **3 agent models**
(`claude-opus-4.8`, `gpt-5.5`, `claude-sonnet-5`) and graded by a **cross-family judge that is never
the executing model** (opus→gpt-5.5, gpt-5.5→opus-4.8, sonnet-5→opus-4.8). 294 jobs, `runs=1`,
verdict-warn-only. Pass line = +10% improvement over baseline.

---

## TL;DR

**What we did.** Turned the single-model, self-judged gate into a 3-model sweep with a non-self,
cross-family judge, then ran it over the entire skill catalog to see how grading actually moves.

**Key insights (all proven from the 294 per-job result files):**

1. **The single-model, single-run gate is unstable.** **49 of 98 skills (50%) produced inconsistent
   one-shot verdicts across the three executor/judge arms.** Only **9** pass on all three; **40** fail
   on all three. This is strong evidence the old 1-model gate was fragile — but with `runs=1` these
   flips **cannot be cleanly attributed to the executor model**; run-to-run variance, judge variance,
   and threshold-adjacency are confounded with true model-sensitivity (see caveats).
2. **No model clearly dominated in this one-run sweep.** Best edit per skill: **gpt-5.5 39, sonnet-5
   35, opus-4.8 24**; pass rates **sonnet-5 38%, gpt-5.5 34%, opus-4.8 29%** — differences that likely
   *overlap* at n=98 without CIs. The Opus-executor/GPT-judge arm had the lowest observed mean, but
   because executor and judge are **not fully crossed**, read this as an *arm-level* result, not "Opus
   executes worse." The clean takeaway: judge choice matters, so a non-self judge is worth having.
3. **Model/arm choice has double-digit leverage.** Per-skill score spread across arms reaches **100+
   points** (e.g. microbenchmarking: sonnet −54.6 vs gpt +53.4). Judge-only swaps on *identical*
   outputs already moved aggregate scores 6–16 pp (§7.3); adding the agent-model axis widens that.
4. **Robust to scale.** 294/294 completed, **0 failures, 0 rate-limit errors**, even at ~9-way
   parallelism. (This is the only *proven* claim here — the rest are evidence, not proof.)
5. **`runs=5` changes verdicts, not just precision (new — §8).** A 5-skill × 3-model `runs=5` batch
   flipped **3 of 15 verdicts** and, for the _identical_ Opus/GPT models, moved single-run point
   estimates by up to **16 pts** (maui/gpt 9.3→−6.9). n=5 adds real 95% CIs and a CI-aware pass bar
   that promotes _significant_ wins and rejects _high-variance_ ones (binlog/sonnet went 12.4→15.5 yet
   flipped it ✅→✗). 15/15 done, 1 transient rate-limit (auto-recovered).
6. **The new gate costs ~15× the tokens (new — §9).** 3 executors × `runs=5` = **15× executor passes**;
   measured at **101.9 M tokens for 5 skills** vs ~6.8 M for a **mean single-leg cross-judge proxy**
   (see §9.5 — this is _not_ the true old self-judged gate; bias direction is ambiguous). The
   **cross-family judge is 37.6% of tokens** (not priced), and **in this routing Opus-judge legs used
   ~2× the judge tokens of the GPT-judge leg**. A full n=5 3-model catalog sweep extrapolates to
   **≈2.0 B tokens, order-of-magnitude** (per-skill range 8.9 M–35.1 M; vs ~133 M proxy-old). Biggest ROI
   red flag: **author-component burned the most tokens (35.1 M) for the worst score (mean −13.2, 0/3)** —
   a gate-fail/rework-investigation candidate, not a proven-harmful verdict.

**Things to watch out for:**

- ⚠️ **`runs=1` → confounded, no confidence intervals.** A one-shot verdict disagreement cannot be
  distinguished from stochastic run variance, judge variance, or threshold noise. At ~30–40% pass
  rates, three arms with the *same* underlying pass probability would still disagree often. So "49/98"
  proves **instability**, not **executor-caused** sensitivity. **Do not action a single skill's number**
  without repeating each `(skill, executor, judge)` 3–5× and reporting per-skill variance/CIs.
- ⚠️ **Executor and judge effects are not separated (design is not fully crossed).** A model's mean
  blends *how it executes* with *how strict its assigned judge is*. Opus's lower mean is partly gpt-5.5
  (its judge) grading harder — not proof Opus edits worse. Firm this up with a fully-crossed re-judge
  (every output by every judge) + a judge-calibration/agreement report (Cohen's κ, Spearman).
- ⚠️ **Pass-rate gaps likely overlap at n=98.** Sonnet 38% / GPT 34% / Opus 29% is *suggestive only*;
  no paired significance test (McNemar / paired bootstrap) has been run. Don't rank models on this yet.
- ⚠️ **History is not poolable.** Old `dotnet/skills-data` scores were opus-4.6 **self-judged**; new
  scores are cross-judged on newer agents. A fixed skill can move ≥10 pp from the judge change alone —
  segment history by `(agent, judge, judge-mode)`, never diff 1:1.
- ⚠️ **40 unanimous fails = strong candidates, not confirmed broken.** Unanimous one-shot fail is
  better evidence than disagreement, but could still be a hard skill, an under-specified test, an
  overly strict judge, or a weak baseline. Triage (rerun + inspect failure *reasons*) before "fixing."
- ⚠️ **Gating blast radius doubled** (1 model → 1 agent + 1 distinct judge). A fail-fast model preflight
  now guards this, but two live model dependencies must both be available.

---

## 1. Run summary

| Metric | Value |
|---|---|
| Targets (skills + agents) | 98 (94 skills + 4 agents) |
| Agent models | claude-opus-4.8, gpt-5.5, claude-sonnet-5 |
| Judge rule | cross-family, never self (opus→gpt-5.5, gpt-5.5→opus-4.8, sonnet-5→opus-4.8) |
| Total jobs | **294** |
| Completed / Failed | **294 / 0** |
| Rate-limit errors | **0** |
| Runs per job | 1 (`--verdict-warn-only`) |
| Peak throughput | ~128 jobs/hr (3 claim-locked drivers, ~9-way parallel) |
| Reliability note | Survived an overnight host-sleep wedge; recovered by killing orphans + relaunch |

---

## 2. Per-model scoreboard (n=98 each)

| Agent model | Pass rate | Mean improvement | Median |
|---|---|---|---|
| **claude-sonnet-5** | **37 / 98 (38%)** | **+6.8%** | **+9.4%** |
| **gpt-5.5** | **33 / 98 (34%)** | **+5.8%** | **+8.7%** |
| **claude-opus-4.8** | **28 / 98 (29%)** | **+3.3%** | **+2.6%** |

**Best-edit-per-skill:** gpt-5.5 = 39, claude-sonnet-5 = 35, claude-opus-4.8 = 24.

> **Read as arm-level, not a model ranking.** Executor and judge are not fully crossed (each executor
> has a fixed cross-family judge), and with `runs=1` there are no CIs. Differences here are indicative
> only; a paired, repeated, fully-crossed re-run is needed before ranking models.

## 3. Cross-model agreement (n=98 targets)

Verdict *(in)*consistency across the three one-shot executor/judge arms. Disagreement = evidence the
gate is arm/run-sensitive; it does **not** by itself prove the *executor model* is the cause.

| Outcome | Count | Share |
|---|---|---|
| 🟡 Pass/fail **disagreement** across arms | **49** | 50% |
| 🟢 Unanimous PASS (all 3) | 9 | 9% |
| 🔴 Unanimous FAIL (all 3) | 40 | 41% |

---

## 4. Per-plugin mean improvement & pass counts

| Plugin | Skills | Opus mean (pass) | Sonnet mean (pass) | GPT mean (pass) |
|---|---|---|---|---|
| dotnet | 1 | 34.2% (1/1) | 21.9% (1/1) | -5.9% (0/1) |
| dotnet-advanced | 3 | -3.4% (0/3) | -12.5% (0/3) | 12.3% (2/3) |
| dotnet-ai | 5 | 9.7% (2/5) | 7% (2/5) | 18.7% (3/5) |
| dotnet-aspnetcore | 4 | 13.2% (1/4) | 26% (3/4) | 9.4% (1/4) |
| dotnet-blazor | 9 | -3.5% (1/9) | 3.9% (6/9) | 7% (5/9) |
| dotnet-data | 1 | -23.2% (0/1) | -7.5% (0/1) | -25.5% (0/1) |
| dotnet-diag | 7 | 0.5% (2/7) | -1.8% (2/7) | 14.6% (2/7) |
| dotnet-experimental | 3 | 12.2% (1/3) | 16.4% (2/3) | 14.9% (2/3) |
| dotnet-maui | 8 | 5.7% (2/8) | 13.2% (3/8) | 13.7% (3/8) |
| dotnet-msbuild | 19 | -2.3% (6/19) | 3.3% (8/19) | -14.6% (3/19) |
| dotnet-nuget | 1 | 34% (1/1) | 27% (0/1) | -5.8% (0/1) |
| dotnet-template-engine | 6 | 16.9% (2/6) | 10.6% (1/6) | 12.1% (2/6) |
| dotnet-test | 18 | 4.8% (5/18) | 9.5% (6/18) | 13.1% (7/18) |
| dotnet-test-migration | 6 | 7.7% (2/6) | 11.3% (2/6) | 20.5% (2/6) |
| dotnet-upgrade | 6 | -6.3% (2/6) | -2.4% (1/6) | -5.6% (1/6) |
| dotnet11 | 1 | -24.3% (0/1) | -3.5% (0/1) | -4.8% (0/1) |

---

## 5. Biggest cross-model score spreads (Top 12)

The skills where the executing model matters most — these dominate the "50% disagreement" finding.

| Skill | Opus | Sonnet | GPT | Spread (pp) |
|---|---:|---:|---:|---:|
| dotnet-diag/microbenchmarking | 7.2 | -54.6 | 53.4 | **108.0** |
| dotnet-msbuild/build-parallelism | 21.5 | 86.8 | -6.8 | 93.6 |
| dotnet-msbuild/resolve-project-references | -20.1 | -10.9 | -88.9 | 77.9 |
| dotnet-msbuild/build-perf-baseline | -100.0 | -25.2 | -78.7 | 74.8 |
| dotnet-advanced/nuget-trusted-publishing | 1.6 | -37.3 | 35.0 | 72.3 |
| dotnet-template-engine/template-instantiation | -56.3 | 10.3 | -56.8 | 67.1 |
| dotnet-blazor/support-prerendering | -13.6 | -64.8 | -5.9 | 58.9 |
| dotnet-msbuild/property-patterns | 55.7 | 22.0 | -0.3 | 55.9 |
| dotnet-maui/dotnet-maui-doctor | 4.0 | 35.7 | 56.5 | 52.5 |
| dotnet-ai/technology-selection | 8.5 | -9.3 | 40.5 | 49.8 |
| dotnet-blazor/configure-auth | -32.0 | 10.6 | 15.5 | 47.5 |
| dotnet-upgrade/dotnet-aot-compat | -50.1 | -9.1 | -54.9 | 45.8 |

---

## 6. Full per-skill results (all 98)

Scores are % improvement over baseline; ✅ = passed (≥+10%), — = failed. **Best** = highest-scoring
model. Flag: 🟢 all pass · 🟡 mixed · 🔴 all fail.

| Skill | Opus | Sonnet | GPT | Best | Flag |
|---|---:|---:|---:|---|:--:|
| dotnet/setup-local-sdk | 34.2 ✅ | 21.9 ✅ | -5.9 — | opus | 🟡 |
| dotnet-advanced/csharp-scripts | -1 — | 4.9 — | 11.8 ✅ | gpt | 🟡 |
| dotnet-advanced/dotnet-pinvoke | -10.8 — | -5 — | -9.8 — | sonnet | 🔴 |
| dotnet-advanced/nuget-trusted-publishing | 1.6 — | -37.3 — | 35 ✅ | gpt | 🟡 |
| dotnet-ai/mcp-csharp-create | -5.8 — | -11.5 — | 8.9 — | gpt | 🔴 |
| dotnet-ai/mcp-csharp-debug | 0.1 — | -15.4 — | 7.5 — | gpt | 🔴 |
| dotnet-ai/mcp-csharp-publish | 16 ✅ | 24.2 ✅ | 11.7 ✅ | sonnet | 🟢 |
| dotnet-ai/mcp-csharp-test | 29.6 ✅ | 46.9 ✅ | 24.9 ✅ | sonnet | 🟢 |
| dotnet-ai/technology-selection | 8.5 — | -9.3 — | 40.5 ✅ | gpt | 🟡 |
| dotnet-aspnetcore/configuring-opentelemetry-dotnet | 9.4 — | 4.9 — | -7.7 — | opus | 🔴 |
| dotnet-aspnetcore/convert-blazor-server-to-webapp | 5.6 — | 46.8 ✅ | 49.2 ✅ | gpt | 🟡 |
| dotnet-aspnetcore/dotnet-webapi | 47.1 ✅ | 40.3 ✅ | 2.5 — | opus | 🟡 |
| dotnet-aspnetcore/minimal-api-file-upload | -9 — | 12.2 ✅ | -6.6 — | sonnet | 🟡 |
| dotnet-blazor/author-component | -3.3 — | -14.7 — | 0.3 — | gpt | 🔴 |
| dotnet-blazor/collect-user-input | 2.4 — | 8.1 — | -12.5 — | sonnet | 🔴 |
| dotnet-blazor/configure-auth | -32 — | 10.6 ✅ | 15.5 ✅ | gpt | 🟡 |
| dotnet-blazor/coordinate-components | -4.2 — | 27.3 ✅ | -1.4 — | sonnet | 🟡 |
| dotnet-blazor/create-blazor-project | 1.6 — | 11.4 ✅ | 12.2 ✅ | gpt | 🟡 |
| dotnet-blazor/fetch-and-send-data | 0.1 — | 14.2 ✅ | 15.1 ✅ | gpt | 🟡 |
| dotnet-blazor/plan-ui-change | 16.9 ✅ | 31.3 ✅ | 29 ✅ | sonnet | 🟢 |
| dotnet-blazor/support-prerendering | -13.6 — | -64.8 — | -5.9 — | gpt | 🔴 |
| dotnet-blazor/use-js-interop | 0.7 — | 12 ✅ | 10.4 ✅ | sonnet | 🟡 |
| dotnet-data/optimizing-ef-core-queries | -23.2 — | -7.5 — | -25.5 — | sonnet | 🔴 |
| dotnet-diag/analyzing-dotnet-performance | -13.5 — | 11.4 — | 22 ✅ | gpt | 🟡 |
| dotnet-diag/android-tombstone-symbolication | -27.8 — | -17.8 — | -16.3 — | gpt | 🔴 |
| dotnet-diag/apple-crash-symbolication | -9 — | -11 — | 7.8 — | gpt | 🔴 |
| dotnet-diag/clr-activation-debugging | 21.4 ✅ | 28.2 ✅ | 23.2 — | sonnet | 🟡 |
| dotnet-diag/dotnet-trace-collect | 21.1 ✅ | 19.6 — | 9.7 — | opus | 🟡 |
| dotnet-diag/dump-collect | 4.5 — | 11.8 ✅ | 2.1 — | sonnet | 🟡 |
| dotnet-diag/microbenchmarking | 7.2 — | -54.6 — | 53.4 ✅ | gpt | 🟡 |
| dotnet-experimental/exp-mock-usage-analysis | -4 — | 4.9 — | 8 — | gpt | 🔴 |
| dotnet-experimental/exp-simd-vectorization | 14.9 — | 20.3 ✅ | 18 ✅ | sonnet | 🟡 |
| dotnet-experimental/exp-test-maintainability | 25.6 ✅ | 23.9 ✅ | 18.7 ✅ | opus | 🟢 |
| dotnet-maui/dotnet-maui-doctor | 4 — | 35.7 — | 56.5 ✅ | gpt | 🟡 |
| dotnet-maui/maui-app-lifecycle | 22.6 ✅ | 15.6 ✅ | 15.5 — | opus | 🟡 |
| dotnet-maui/maui-collectionview | -7.1 — | -7.6 — | -12.5 — | opus | 🔴 |
| dotnet-maui/maui-data-binding | -0.7 — | 6 — | 11.6 ✅ | gpt | 🟡 |
| dotnet-maui/maui-dependency-injection | 2.9 — | -10.1 — | -2.9 — | opus | 🔴 |
| dotnet-maui/maui-safe-area | 34.7 ✅ | 41.5 ✅ | 23.2 ✅ | sonnet | 🟢 |
| dotnet-maui/maui-shell-navigation | 4.4 — | 15.8 ✅ | 8.7 — | sonnet | 🟡 |
| dotnet-maui/maui-theming | -15.4 — | 8.8 — | 9.3 — | gpt | 🔴 |
| dotnet-msbuild/agent.msbuild | -2.8 — | -4 — | -13.9 — | opus | 🔴 |
| dotnet-msbuild/binlog-failure-analysis | 11.1 ✅ | 21.5 ✅ | 7.8 — | sonnet | 🟡 |
| dotnet-msbuild/binlog-generation | 25.2 ✅ | 12.4 ✅ | 22.6 ✅ | opus | 🟢 |
| dotnet-msbuild/build-parallelism | 21.5 ✅ | 86.8 ✅ | -6.8 — | sonnet | 🟡 |
| dotnet-msbuild/build-perf-baseline | -100 — | -25.2 — | -78.7 — | sonnet | 🔴 |
| dotnet-msbuild/build-perf-diagnostics | -71.7 — | -40 — | -61.4 — | sonnet | 🔴 |
| dotnet-msbuild/check-bin-obj-clash | -5.5 — | -2.7 — | -9.8 — | sonnet | 🔴 |
| dotnet-msbuild/directory-build-organization | -13.2 — | 14.5 ✅ | 10.8 ✅ | sonnet | 🟡 |
| dotnet-msbuild/eval-performance | -7.5 — | -22.8 — | -11 — | opus | 🔴 |
| dotnet-msbuild/extension-points | 24.5 ✅ | 25.7 ✅ | 6.2 — | sonnet | 🟡 |
| dotnet-msbuild/including-generated-files | 38.8 ✅ | 16.2 ✅ | -6.5 — | opus | 🟡 |
| dotnet-msbuild/incremental-build | -19.4 — | 13.4 ✅ | -28.4 — | sonnet | 🟡 |
| dotnet-msbuild/item-management | 0.6 — | -15.8 — | 23.7 ✅ | gpt | 🟡 |
| dotnet-msbuild/msbuild-antipatterns | -4.1 — | -10.8 — | -32.2 — | opus | 🔴 |
| dotnet-msbuild/msbuild-modernization | -5.9 — | -3.4 — | 6.2 — | gpt | 🔴 |
| dotnet-msbuild/msbuild-server | 28.7 ✅ | -11 — | -5.9 — | opus | 🟡 |
| dotnet-msbuild/property-patterns | 55.7 — | 22 ✅ | -0.3 — | opus | 🟡 |
| dotnet-msbuild/resolve-project-references | -20.1 — | -10.9 — | -88.9 — | sonnet | 🔴 |
| dotnet-msbuild/target-authoring | 0.4 — | -2.2 — | -11.2 — | opus | 🔴 |
| dotnet-nuget/convert-to-cpm | 34 ✅ | 27 — | -5.8 — | opus | 🟡 |
| dotnet-template-engine/template-authoring | 27 — | -12.8 — | 28.7 — | gpt | 🔴 |
| dotnet-template-engine/template-comparison | 37.9 ✅ | 23.7 — | 30.2 ✅ | opus | 🟡 |
| dotnet-template-engine/template-discovery | 33.9 — | -5.2 — | -10.9 — | opus | 🔴 |
| dotnet-template-engine/template-instantiation | -56.3 — | 10.3 ✅ | -56.8 — | sonnet | 🟡 |
| dotnet-template-engine/template-smart-defaults | 27.7 — | 42.6 — | 39.8 — | sonnet | 🔴 |
| dotnet-template-engine/template-validation | 30.8 ✅ | 5.3 — | 41.9 ✅ | gpt | 🟡 |
| dotnet-test/agent.test-quality-auditor | -6.4 — | 10.7 ✅ | 12.2 ✅ | gpt | 🟡 |
| dotnet-test/agent.testability-migration | -4.9 — | -16.9 — | -2.3 — | gpt | 🔴 |
| dotnet-test/assertion-quality | 2.6 — | 12.8 ✅ | 9.2 — | sonnet | 🟡 |
| dotnet-test/code-testing-agent | 0.6 — | -24.1 — | -0.7 — | opus | 🔴 |
| dotnet-test/coverage-analysis | 3.3 — | 11 — | 29.9 ✅ | gpt | 🟡 |
| dotnet-test/crap-score | 19.1 ✅ | 41.3 — | 45.5 — | gpt | 🟡 |
| dotnet-test/detect-static-dependencies | -7.2 — | 3.9 — | 4.3 — | gpt | 🔴 |
| dotnet-test/filter-syntax | 32.3 — | 15.2 — | 18.3 — | opus | 🔴 |
| dotnet-test/generate-testability-wrappers | -5.1 — | -10.9 — | 33.5 ✅ | gpt | 🟡 |
| dotnet-test/grade-tests | 16.1 ✅ | 24.1 ✅ | 25 ✅ | gpt | 🟢 |
| dotnet-test/migrate-static-to-wrapper | -2.6 — | -20.4 — | -17.3 — | opus | 🔴 |
| dotnet-test/mtp-hot-reload | 29.5 ✅ | 55.4 ✅ | 35.9 ✅ | sonnet | 🟢 |
| dotnet-test/run-tests | 8.5 — | 29.8 ✅ | 24.7 ✅ | sonnet | 🟡 |
| dotnet-test/test-anti-patterns | -25.4 — | -6.9 — | -9.1 — | sonnet | 🔴 |
| dotnet-test/test-gap-analysis | 10.1 ✅ | 31 ✅ | 9.9 — | sonnet | 🟡 |
| dotnet-test/test-smell-detection | -4.6 — | -4.6 — | -3.7 — | gpt | 🔴 |
| dotnet-test/test-tagging | 13.5 ✅ | 9.3 — | 8.2 — | opus | 🟡 |
| dotnet-test/writing-mstest-tests | 7.2 — | 11.1 — | 12.2 ✅ | gpt | 🟡 |
| dotnet-test-migration/agent.test-migration | 10.3 ✅ | 24.4 — | 25.3 ✅ | gpt | 🟡 |
| dotnet-test-migration/migrate-mstest-v1v2-to-v3 | 20.1 ✅ | 24.9 ✅ | 16.4 ✅ | sonnet | 🟢 |
| dotnet-test-migration/migrate-mstest-v3-to-v4 | 2.2 — | 1.8 — | 20.7 — | gpt | 🔴 |
| dotnet-test-migration/migrate-vstest-to-mtp | 3.3 — | 11.3 ✅ | 27.1 — | gpt | 🟡 |
| dotnet-test-migration/migrate-xunit-to-mstest | 4.7 — | 0.6 — | 11.1 — | gpt | 🔴 |
| dotnet-test-migration/migrate-xunit-to-xunit-v3 | 5.4 — | 4.8 — | 22.3 — | gpt | 🔴 |
| dotnet-upgrade/dotnet-aot-compat | -50.1 — | -9.1 — | -54.9 — | sonnet | 🔴 |
| dotnet-upgrade/migrate-dotnet10-to-dotnet11 | 18.9 ✅ | 19.3 ✅ | 1.6 — | sonnet | 🟡 |
| dotnet-upgrade/migrate-dotnet8-to-dotnet9 | 13 ✅ | -21.8 — | 6.2 — | opus | 🟡 |
| dotnet-upgrade/migrate-dotnet9-to-dotnet10 | 0.1 — | 2 — | -7.8 — | sonnet | 🔴 |
| dotnet-upgrade/migrate-nullable-references | -7.4 — | -4.9 — | 7.1 — | gpt | 🔴 |
| dotnet-upgrade/thread-abort-migration | -12.3 — | -0 — | 13.8 ✅ | gpt | 🟡 |
| dotnet11/system-text-json-net11 | -24.3 — | -3.5 — | -4.8 — | sonnet | 🔴 |

---

## 7. Impact analysis (folded in)

> Full detail — infra changes, risk register, correctness evidence, and the isolated judge-swap
> experiments — lives in `impact-analysis.md` (§1–9). Condensed here.

### 7.1 What changed
- **New single source of truth** `eng/eval-models.json` (latest ids, matrix, defaultJudge,
  judgeOverrides) + resolver `eng/resolve-judge.mjs` enforcing **judge ≠ agent**.
- **Workflows:** gating (PR) = 1 agent + resolved cross-judge; nightly = 3-model matrix; artifacts
  keyed `…@<model>`; **fail-fast `preflight-models` job** names any missing model id before the matrix
  fans out.
- **Dashboard/history:** replays + benchmark points re-keyed by model (no cross-model averaging); Vally
  publishes model-keyed history to a new `dashboard-vally-data` branch.
- **Log-privacy fix:** the model preflight no longer dumps the available-model roster to CI logs — on
  failure it names only the *missing required* ids + a withheld count.
- **Core bug fixed incidentally:** both workflows previously ran `MODEL == JUDGE_MODEL`
  (opus-4.6 judging itself). Every leg now has judge ≠ agent.

### 7.2 Risk register (ranked)

| # | Risk | Status | Mitigation |
|---|---|---|---|
| R1 | 3 model ids unverified vs CI token | 🟢 mitigated | fail-fast `preflight-models` (both workflows) names missing ids in seconds |
| R2 | Gating depends on 2 models (agent+judge), not 1 | 🟠 by design | centralized ids; net correctness win (no self-judge) |
| R3 | Nightly cost/throughput ×3 | 🟠 proven | Vally `normalize` collapses shards→plugins (cost = plugins×models) |
| R4 | Legacy 3-field session replays drop off | 🟡 proven | 7-day retention ages them out; optional one-time purge at rollout |
| R5 | Vally history has no dashboard consumer yet | 🟡 proven | data captured now; rendering is follow-up |
| R6 | Dashboard default model = last leg (Sonnet) | 🟢 cosmetic | change default to gating model if preferred |

### 7.3 Isolated experiments (why the judge/model axes matter)
- **Judge swap on byte-identical outputs** moved aggregate score **6–16 pp** (toy fixture): opus-self
  −14.6% → opus/gpt −25.1% (−10.5 pp); gpt-self −24.7% → gpt/opus −8.7% (+16.0 pp); sonnet-self −12.9%
  → sonnet/opus −18.9% (−6.0 pp).
- **Real-skill judge swap (3 skills, outputs fixed on opus-4.8):** skill-level Δ small (−0.2…−4.0 pp),
  **no verdict flipped**; but per-scenario swings hit **−24 … +19 pp**. Clearly-good and clearly-bad
  skills are stable; **near-threshold skills are where flips live.**
- **Agent-model axis (this full run):** far larger — spreads up to 100+ pp — confirming the executing
  model, not just the judge, drives verdicts.

### 7.4 History carry-forward (`dotnet/skills` + `dotnet/skills-data`)
- Eval/token history **preserved** (model-tagged, appended; old opus-4.6 series intact and rendered
  separately). Session replays now model-keyed (legacy files age out in 7 days).
- **Do not pool** old self-judge scores with new cross-judge scores — segment by
  `(agent, judge, judge-mode)`.

---

## 8. n=5 stabilization batch — before/after (2026-07-07)

_A follow-up run that directly attacks §10‑rec‑1 ("deepen at `runs≥5`"). **5 skills × 3 executor models,
`--runs 5`**, same cross-family judges (opus→gpt‑5.5, gpt‑5.5→opus‑4.8, sonnet‑4.6→opus‑4.8),
`--parallel-scenarios 3 --parallel-runs 3`, all 3 legs per skill launched as parallel detached
processes. **15/15 jobs completed, 1 transient rate-limit hit (maui/sonnet, auto-recovered, exit 0),
0 failures**; per-leg wall-clock ≈ 3–14 min.
Results: `exp-n5/<skill>/<model>/<ts>/results.json` (session-state, not committed)._

> **⚠️ The before/after differs on _two_ axes, not one.** "Before" = full-run §6 (`runs=1`, Sonnet =
> `claude-sonnet-5`). "After" = this batch (`runs=5`, Sonnet = `claude-sonnet-4.6`). **Opus‑4.8 and
> GPT‑5.5 are the _identical_ model in both**, so their deltas isolate the pure `runs=1→5` effect.
> **Sonnet changed model _and_ run count**, so its deltas are confounded — do not read them as an
> n-effect. Every "after" number below is a mean of 5 runs with a 95% CI; "before" numbers are single
> runs with no CI.

### 8.1 Per-skill before → after (point estimate %, pass line = +10%)

Pass at `runs=5` requires the point estimate ≥ +10% **and** statistical significance (95% CI excludes
0) — a stricter, CI-aware bar the `runs=1` gate could not apply.

| Skill | Model | Before (n=1) | After (n=5) | 95% CI (n=5) | Verdict Δ |
|---|---|---:|---:|---|---|
| dotnet-msbuild/binlog-generation | opus‑4.8 | 25.2 ✅ | **33.9 ✅** | [21.7, 45.3] | — pass |
| dotnet-msbuild/binlog-generation | gpt‑5.5 | 22.6 ✅ | **19.0 ✅** | [5.2, 30.5] | — pass |
| dotnet-msbuild/binlog-generation | sonnet¹ | 12.4 ✅ | 15.5 ✗ | [−8.5, 27.9] | **✅→✗** (not sig) |
| dotnet-blazor/author-component | opus‑4.8 | −3.3 ✗ | −0.9 ✗ | [−16.1, 9.7] | — fail |
| dotnet-blazor/author-component | gpt‑5.5 | 0.3 ✗ | −1.2 ✗ | [−13.8, 3.0] | — fail |
| dotnet-blazor/author-component | sonnet¹ | −14.7 ✗ | −37.5 ✗ | [−51.5, −18.7] | — fail (now sig‑neg) |
| dotnet-aspnetcore/minimal-api-file-upload | opus‑4.8 | −9.0 ✗ | 3.0 ✗ | [−8.8, 12.0] | — fail |
| dotnet-aspnetcore/minimal-api-file-upload | gpt‑5.5 | −6.6 ✗ | 0.1 ✗ | [−5.2, 14.2] | — fail |
| dotnet-aspnetcore/minimal-api-file-upload | sonnet¹ | 12.2 ✅ | 8.9 ✗ | [3.2, 20.5] | **✅→✗** (<10) |
| dotnet-diag/dump-collect² | opus‑4.8 | 4.5 ✗ | **10.2 ✅** | [0.5, 18.2] | **✗→✅** (sig) |
| dotnet-diag/dump-collect² | gpt‑5.5 | 2.1 ✗ | −3.1 ✗ | [−9.0, 3.7] | — fail |
| dotnet-diag/dump-collect² | sonnet¹ | 11.8 ✅ | 24.7 ✅ | [17.3, 30.4] | — pass |
| dotnet-maui/maui-theming | opus‑4.8 | −15.4 ✗ | −20.8 ✗ | [−22.7, −10.8] | — fail (now sig‑neg) |
| dotnet-maui/maui-theming | gpt‑5.5 | 9.3 ✗ | −6.9 ✗ | [−7.3, 12.2] | — fail |
| dotnet-maui/maui-theming | sonnet¹ | 8.8 ✗ | 6.5 ✗ | [0.9, 14.6] | — fail |

¹ Sonnet row is **confounded** (`sonnet-5`→`sonnet-4.6` _and_ n=1→5). ² `dump-collect` replaced
`dotnet-ai/technology-selection` from the original 5-skill plan: technology-selection invokes
rubber-duck subagents on every arm (~20 min/scenario × 6 × 3 models ≈ 5–6 h), so it was swapped for a
non-subagent, mixed-outcome skill of equal reporting value. (Its opus‑4.8 leg did complete at 6.8%.)

### 8.2 What the numbers prove

**Proven (direct evidence above):**

1. **`runs=1` point estimates are unstable by double digits — even for the _same_ model+judge.** Because
   Opus and GPT are identical models before/after, their movement is _pure_ run-count/variance. The
   largest of the 10 same-model legs: **maui-theming/gpt‑5.5: 9.3 → −6.9 (−16.2 pt)**, turning a
   near-pass into a clear fail; minimal-api/opus **−9.0 → +3.0 (+12.0)**. _Caveat:_ the "after" value is
   a 5‑run **mean**, not a second single draw, and these are the extreme legs — so this shows single-run
   estimates _can_ move materially, **not** a calibrated "every run lands in a ±15 pt band." Mean |Δ|
   across the 10 identical-model legs = ~6.8 pt (Opus) / ~6.6 pt (GPT).
2. **`runs=5` yields _estimated_ 95% CIs; `runs=1` reported none.** Every after-row carries a CI (e.g.
   binlog/opus [21.7, 45.3]; maui/opus [−22.7, −10.8]). _Caveat:_ at n=5 the CI is a small-sample
   estimate whose width depends on the estimator and run independence — treat it as directional, not
   exact. The point stands that `runs=1` gave the gate _no_ uncertainty estimate at all.
3. **The gate is CI-aware and therefore stricter _by construction_.** binlog/sonnet **went _up_
   (12.4→15.5) yet flipped ✅→✗** because its CI [−8.5, 27.9] crosses 0; dump-collect/opus flipped ✗→✅
   because 10.2 is significant (CI [0.5, 18.2]). Every after-verdict is internally consistent with the
   rule "point ≥ +10 AND CI excludes 0" (independently checked). _Caveat:_ the flips are **not
   attributable to the CI rule alone** — point estimates also moved between runs (and Sonnet changed
   model), so "stricter" is proven _relative to point-only logic_, not isolated from those effects.
4. **Robust at parallel scale.** Per the run metadata (`exp-n5/status-legs.jsonl` + the 294-job
   `status/*.json`): **15/15 legs completed, 0 non-zero exits, 1 transient rate-limit hit**
   (maui/sonnet, auto-recovered), 3 legs/skill in parallel. (This claim rests on the status logs, not
   the score table.)

**Inferred (reasoning, not proof):**

- The two Sonnet flips (binlog ✅→✗, minimal-api ✅→✗) are **triple-confounded** — the `sonnet-5 →
  sonnet-4.6` model swap, the new CI-aware bar, _and_ stochastic run averaging all move together.
  Attribution to run count alone is invalid; dump-collect/sonnet _rose_ 11.8→24.7 under the same
  confound.
- Net pass count over these 15 legs went **5 → 4**. This is **weak** evidence only: with the Sonnet
  model changed, point estimates shifted, and n=15, a one-pass drop cannot quantify how much stricter
  the gate is catalog-wide. The gate is stricter _by definition_; the net count does not measure the
  magnitude.

### 8.3 Bottom line

At the same threshold, moving `runs=1 → runs=5` **did not just add precision — it changed verdicts**,
and it did so _for the same models_. The single clean Opus flip (dump-collect ✗→✅, now significant)
and the binlog/sonnet ✅→✗ "went up but lost the pass" case are the two archetypes the CI-aware gate is
designed to correct: promote _significant_ wins, reject _high-variance_ ones. This is direct empirical
support for §10‑rec‑1 — **skills near the ±10% line must be gated at `runs≥5` with CIs**, never on a
single draw.

---

## 9. Cost, token & efficiency analysis (n=5 batch)

_Answers: (a) what does the new gate cost vs the old one, and (b) which skills don't earn their tokens.
All numbers from the validator's own `results.json → scenarios[].{baseline,skilledIsolated,
skilledPlugin}.metrics` (input/output/cache + judge tokens, `wallTimeMs`, `toolCallCount`)._

### 9.1 Token accounting — method & validation

`.metrics` is **one representative run** per arm (`turnCount=1` on baseline confirms it; a leg has
`scenarios × 5 runs × 3 arms` sessions on disk — e.g. dump-collect/opus = 9×5×3 = **135** session
dirs). So **leg total = Σ(metrics) × 5 runs**. _Validated:_ summing `assistant.message.outputTokens`
across all 135 raw session logs for dump-collect/opus = **218,658**, vs `metrics.agentOut × 5 =
218,500` — **ratio 1.00** (proven). Figures below are input+output tokens; `cacheReadTokens`
(≈8.3M/rep-run across the batch) is reported separately and excluded from the totals.

### 9.2 The 15× multiplier — new gate vs previous setup

"Previous setup" = the old **single executor model, `runs=1`, self-judged** gate. "New" = **3
executors × `runs=5` × cross-family judge**. Structurally that is **3 × 5 = 15×** executor passes;
the measured tokens confirm it. (Old-proxy = the mean single-model, single-run cost incl. its judge;
a true self-judge's judge-token count would differ slightly by model — see §9.3.)

| Skill | New total (n=5, 3-model) | Old-proxy (1-model, 1-run) | × | Scenarios |
|---|---:|---:|---:|---:|
| dotnet-blazor/author-component | **35.1 M** | 2.34 M | 15× | 5 |
| dotnet-diag/dump-collect | **32.3 M** | 2.15 M | 15× | 9 |
| dotnet-msbuild/binlog-generation | 13.3 M | 0.89 M | 15× | 3 |
| dotnet-maui/maui-theming | 12.3 M | 0.82 M | 15× | 4 |
| dotnet-aspnetcore/minimal-api-file-upload | 8.9 M | 0.59 M | 15× | 3 |
| **Total (5 skills)** | **101.9 M** | **6.8 M** | **15×** | — |

**Wall-clock (proven, `status*.jsonl`):** legs ran **160–848 s**; the whole 15-leg batch finished in
~14 min wall thanks to 3-way leg parallelism + internal `parallel-scenarios/runs 3`. For contrast,
the **swapped-out `technology-selection` opus leg alone took 5,314 s (88 min)** — the subagent outlier
that motivated the swap (§8, note ²).

### 9.3 Where the tokens go — agent vs judge

Across the batch (per-rep-run, summed): **agent 12.7 M / judge 7.65 M → the cross-family judge is
37.6% of all tokens** (share of _tokens_, not dollars — model-specific pricing not applied). And the
judge token count is asymmetric by leg:

| Executor leg | Judge model | Agent tok | Judge tok |
|---|---|---:|---:|
| opus-4.8 | gpt-5.5 | 5.05 M | **1.43 M** |
| gpt-5.5 | opus-4.8 | 3.95 M | **3.26 M** |
| sonnet-4.6 | opus-4.8 | 3.73 M | **2.96 M** |

**In this routing, Opus-judge legs used ~2× the judge tokens of the GPT-judge leg** (3.26 M & 2.96 M
vs 1.43 M). ⚠️ **Confounded:** GPT only ever judged Opus-executor output and Opus only ever judged
GPT/Sonnet output, so judge-token differences mix judge model with the length/failure-mode of the
_executor output being graded_. This is **not** evidence that "Opus judge is 2× pricier per unit work."
Since the **default judge is Opus** for two of three legs, judge overhead does land on the priciest
model — but proving a GPT-judge saving needs a **crossed design** (same executor artifacts judged by
both Opus and GPT). — _inferred, under-identified._

### 9.4 Which skills aren't earning their tokens (ROI)

Combining §8 scores with §9 cost. "K tok / pt" = new-total tokens per point of the **best** model's
improvement (lower = better value; n/a when best ≤ 0).

| Skill | Best | Mean | Pass legs | New cost | K tok/pt | Verdict |
|---|---:|---:|:--:|---:|---:|---|
| binlog-generation | 33.9 | 22.8 | **3/3** | 13.3 M | **392 K** | ✅ **earns it** — best value in the set |
| dump-collect | 24.7 | 10.6 | 2/3 | 32.3 M | 1.31 M | ✅ helps, but pricey (9 scenarios) |
| minimal-api-file-upload | 8.9 | 4.0 | 0/3 | 8.9 M | 998 K | ⚠️ marginal — no leg clears +10% |
| maui-theming | 6.5 | **−7.1** | 0/3 | 12.3 M | 1.89 M | 🔴 **gate-fail** — negative mean, 0 pass |
| author-component | −0.9 | **−13.2** | 0/3 | **35.1 M** | n/a (≤0) | 🔴 **worst ROI** — most tokens, 0 pass |

**Flags (0/3 pass is proven; "harmful" is inferred):** `author-component` consumed the **most tokens
of any skill (35.1 M)** and returned the **worst score (mean −13.2, 0/3, Sonnet −37.5 significant-
negative)**. `maui-theming` is the second negative-mean, 0-pass skill. `minimal-api-file-upload` clears
nothing at the +10% bar. These three are the batch's **gate-fail / rework-investigation candidates**,
consistent with the full-run §6 "unanimous/near-unanimous fail" set. `binlog-generation` and
`dump-collect` clearly justify their cost.

⚠️ _With only 3 executor models, "0/3" and "2/3" are a fixed 3-model panel, not a sample of a model
population — don't read them as statistical generalization across "models."_

_Caveat (inferred):_ a negative score can also indict the **test/baseline** (too-strong baseline,
mis-specified assertions, or an over-strict judge), not only the skill. Before deleting a skill, read
the per-run failure `reason`s — but a skill this expensive with a **significant** negative (Sonnet
author-component) is a strong "fix or cut" signal.

### 9.5 Implications for the replace decision

- **Budget:** at ~**20.4 M tokens/skill** mean, a full **n=5 × 3-model** sweep of all 98 skills is
  **≈ 2.0 B tokens, order-of-magnitude** (per-skill spread 8.9 M–35.1 M scales with scenario count 3–9),
  vs ≈ **133 M** for the **proxy-old** single-leg gate. The **NEW/OLD = 15×** is structural (3 executors ×
  5 runs) and confirmed in the token ledger; the "vs old" side is a **proxy**, not the historical
  self-judged gate (see caveat below), so read 15× as "vs an equivalent single-leg cross-judge run,"
  and the absolute old figure as approximate.
- **⚠️ OLD-proxy caveat:** the real old gate was **self-judged** (executor == judge); the proxy uses a
  **cross-family** judge, so its judge-token count differs. Bias direction is **ambiguous** — if the old
  self-judge was cheaper, 15× understates the true multiplier; if costlier, it overstates. A precise
  number needs replaying the actual old self-judge config.
- **What the 15× buys:** confidence intervals, significance-gated verdicts, and cross-model signal —
  i.e. the stability the old gate lacked (§8). The question is not "is it more expensive" (it is,
  ~15×) but "where do we spend it."
- **Candidate knobs (hypotheses, not yet proven savings):** (1) **try GPT-5.5 as default judge** — judge
  is 37.6% of _tokens_ and lands on Opus today, but the "~halve" figure is **not** established (only
  Opus-executor output was GPT-judged; needs a crossed A/B before claiming a saving); (2) **gate at n=5
  only for skills near the ±10% line**, run
  the clearly-passing/failing ones at **n=3** (skip n=1; most of the 40 unanimous fails don't need n=5 to confirm);
  (3) **investigate/rework the gate-fail skills** (author-component, maui-theming) before paying 15× to
  re-confirm they fail. Combined, these can reduce the ≈2.0 B, but treat (1) as an unproven lever until
  a crossed judge A/B is run.

---

## 10. Caveats & recommended next steps

1. **Deepen the 49 disagreement skills at `runs≥5`** to separate true model-sensitivity from `runs=1`
   noise and attach confidence intervals before acting on any single skill.
2. **Disentangle judge strictness from execution quality** — re-judge a fixed model's outputs across
   all judges to quantify the judge's contribution to each model's mean.
3. **Triage the 40 unanimous fails** into (a) genuinely hard, (b) toy/near-zero-value fixtures where a
   negative score is correct, (c) actually-broken skills.
4. **Verify the 3 model ids against the CI token** (now automated via preflight) before flipping
   gating.
5. **History:** one-time session-branch purge at rollout; keep new scores segmented from old.
6. **Cost controls (new, from §9):** (a) **test** GPT-5.5 as default judge — judge is 37.6% of tokens
   and lands on Opus today, but the saving is a hypothesis (needs a crossed judge A/B, not the current
   uncrossed routing); (b) tiered runs — n=5 only near the ±10% line, **n=3** for unanimous pass/fail;
   (c) investigate/rework gate-fail skills (author-component 35.1 M tok / −13.2 mean, maui-theming)
   before paying 15× to re-confirm they fail.
7. **Add token/cost to the results schema surfaced by CI.** `results.json` already carries per-arm
   `metrics` (tokens, `wallTimeMs`); expose an aggregated per-run token+cost line in the reporter so
   the gate reports _spend_ alongside _score_ without re-parsing session logs.

_Artifacts (session-state, not committed): `fullrun/scoreboard_jobs.csv` (294 rows),
`fullrun/scoreboard_pivot.csv` (98 rows), `fullrun/status/*.json` (authoritative),
`impact-analysis.md` (§1–9), and the n=5 batch: `exp-n5/<skill>/<model>/<ts>/results.json` (15 legs),
`exp-n5/parsed-n5.json` (parsed scores+CIs), `exp-n5/status-legs.jsonl` (run metadata)._

## 11. Sonnet-as-default-judge swap — controlled re-judge (2026-07-07)

**Change under test.** Move the default judge from **Opus 4.8** to **Sonnet 4.6**
(`eng/eval-models.json`: `defaultJudge = claude-sonnet-4.6`, `judgeOverrides =
{claude-sonnet-4.6: gpt-5.5}`). New routing (selftest-verified): `opus→sonnet`,
`gpt→sonnet`, `sonnet→gpt`. Opus is removed as a judge; Sonnet judges 2 of 3 arms.

**Method — isolate the judge, hold outputs fixed.** I re-judged the *already-generated*
`gpt-5.5`-executor outputs (the cleanest leg: old judge = Opus, new judge = Sonnet on
byte-identical outputs) using the newly-registered `rejudge` command. Three legs per skill
where available: **(A)** Opus-original (from the live n=5 run), **(B)** Sonnet-rejudge,
**(C)** Opus-rejudge *control* (same model, fresh run — measures run/path noise). This
required registering the dormant `rejudge`/`consolidate` commands in `Program.cs` (they
existed in source but were never wired into the CLI) and rebuilding the validator.

### 11.1 Scores — Sonnet vs Opus on identical `gpt-5.5` outputs

| Skill | Opus-orig % (CI) | Sonnet % (CI) | Verdict Opus→Sonnet | Δ pt |
|---|---|---|---|---|
| binlog-generation | **18.96** (5.2, 30.5) ✅sig | **11.76** (0.95, 26.5) ✅sig | pass → **pass (fragile)** | −7.2 |
| minimal-api-file-upload | 0.08 (−5.2, 14.2) ✗ | 5.68 (−2.2, 17.2) ✗ | fail → fail | +5.6 |
| maui-theming | −6.90 (−7.3, 12.3) ✗ | −7.16 (−9.9, 2.3) ✗ | fail → fail | −0.3 |
| dump-collect | −3.15 (−9.0, 3.7) ✗ | −5.84 (−10.9, 0.6) ✗ | fail → fail | −2.7 |
| author-component | −1.2* ✗ | *(rejudge hung, incomplete)* | — | — |

*author-component Sonnet leg stalled on a hung judge call and is excluded.*

**Proven (identical outputs, judge-only change):**
- **No verdict flipped in the 4 completed skills.** The one passing skill stayed a pass; the
  three fails stayed fails.
- **Sonnet grades the one strong/passing skill (binlog) materially lower.** Point estimate
  −7.2 pt vs Opus-original, or **−9.4 pt vs the Opus *control*** (21.18; the path-matched
  baseline). Its CI-low collapses **5.2 → 0.95** — binlog becomes a *fragile* pass, one
  perturbation from flipping.
- **No simple uniform lenient/strict offset.** Deltas are mixed in sign (+5.6, −7.2, −0.3,
  −2.7).

**Inferred / not established (per cross-family review):**
- The −9.4 pt binlog effect is *suggestive of a real judge-model difference* (it is large vs
  the single same-model control delta of +2.2 pt), but is **not statistically established** —
  the per-model CIs overlap and I have no *paired* CI on the Sonnet−Opus difference, and only
  one control run. Treat as "watch," not "proven."
- With only 4 skills — one pass, three weak fails — this sample has **low power** to detect
  verdict instability, and it under-samples the risky zone (marginal passes near +10% with
  CI-low near 0), which is exactly where Sonnet's downward pressure on strong skills would
  bite. Do **not** read this as "Sonnet is safe as default judge" generally.

### 11.2 Token cost — **unresolved by this experiment** (headline correction)

I initially read a 38–57% token *saving* for Sonnet. **The Opus control refutes that as an
artifact.** Re-judging binlog with **Opus** (same model) cost **144,646** judge tokens vs
**324,620** in the original live run — a **−55%** drop from the *rejudge code path alone*,
independent of model. So any "Sonnet vs original" comparison measures the path, not the model.

The only path-matched, apples-to-apples pair (both via `rejudge`) is binlog:

| binlog leg | judge input | judge output | total |
|---|---|---|---|
| Opus control (rejudge) | 134,022 | 10,624 | **144,646** |
| Sonnet (rejudge) | 186,775 | 14,334 | **201,109** |

On that single clean pair, **Sonnet used ~39% *more* judge tokens, not fewer.** Two reasons I
**cannot** promote this to a finding: (1) the token totals are from *one representative run*
of five, and the sampled run may differ leg-to-leg; (2) the input-token gap (134K vs 187K) is
**not** explained by retries — the Opus leg had *more* retries (6 vs 1) yet *fewer* input
tokens — so it is either cross-model usage-accounting differences or non-comparable sampled
runs. Either way the measurement is not clean.

> **Bottom line on cost: unresolved.** This rejudge experiment cannot measure judge-model cost
> because the rejudge path is far cheaper than the live-eval path (−55% same-model) and per-run
> token sampling is too noisy. The one clean data point leans *more* expensive for Sonnet, not
> less. A decision-grade cost number needs a purpose-built A/B (repeated full evals, fixed
> executor outputs, per-call token capture, price-weighted).

### 11.3 Recommendation

- **Config is correct and low-risk to keep** (selftest passes; routing removes self-judging;
  the same-family pairing count is unchanged from the Opus-default setup, so no regression).
- **Adopt-with-monitoring, don't declare victory.** The decision-relevant risk is that Sonnet
  compresses strong-skill scores downward (binlog −9.4 pt, fragile pass), which is most
  dangerous for **marginal passes** — under-sampled here.
- **Before flipping the default in CI**, run the decision-grade measurement the rubber-duck
  prioritized: 3–5 repeated Opus *and* Sonnet rejudges on the *same serialized prompts* for a
  set that includes several **near-threshold** skills; compute a **paired** Sonnet−Opus CI and
  a threshold-flip rate; and capture per-call, price-weighted cost. Verify prompt/call identity
  by hash to close the input-token discrepancy.

_Judge-swap artifacts (session-state / `C:\temp\rejudge-n5`, not committed): per-leg
`results.orig.json` (Opus-original) + `results.json` (rejudged) + `rejudge.log`; rebuilt
validator with `rejudge`/`consolidate` at `C:\temp\sv-rejudge\skill-validator.dll`. Config
changes (`eval-models.json`, `resolve-judge.mjs` doc, `vally-evaluation.yml` comment,
`run-vally-evals.sh` fallback, `Program.cs` command registration) are in the working tree,
**not committed**._

---

## 12. Full-catalog `n=5` attempt on the remaining 93 skills — blocked at scale (2026-07-08)

_Goal: continue the old-routing sweep (`opus→gpt`, `gpt→opus`, `sonnet→opus`) on the
remaining **93 skills** after the successful 5-skill `n=5` batch. Shape: 5 child batches,
roughly 17–19 skills each, each asked to run the 3 executor legs with `--runs 5`. This
section records what actually happened overnight._

### 12.1 What the batch artifacts show

| Batch | Skills | Latest state | Direct proof | `results.json` |
|---|---:|---|---|---|
| batch 1 | 19 | **running / retried 3 times** | `batch-summary.json`: attempt1 hit repeated `session-state\\events.jsonl` mutex timeouts; attempt3 relaunched at `--parallel-skills 1 --parallel-scenarios 1 --parallel-runs 1`, PID `54504`, reached `analyzing-dotnet-performance` | **none** |
| batch 2 | 19 | **running / likely stale** | `batch-summary.json`: auto-restarted to `1/2/1`, PID `16956`, advanced beyond the original 4 skills to 6 distinct skills (`crap-score` … `dotnet-aot-compat`); last proven log write `2026-07-08T09:10:30-07:00` | **none** |
| batch 3 | 19 | **all 3 sweeps relaunched, still running** | `batch-summary.json`: original `opus→gpt` attempt failed on `events.jsonl` mutex timeout; 3 relaunched sweeps active at `1/2/1` with wrapper/child PIDs recorded; `results_json_count = 0` | **none** |
| batch 4 | 19 | **2 sweeps completed-no-results; 1 stalled/restarted** | `batch-summary.json`: `opus→gpt` exited `4294967295` with no results; `gpt→opus` exited `0` with no results; `sonnet→opus` restarted at `1/1/1`; a monitored **STALL** event fired on `opus→gpt` after **6 completed scenarios** with no new log writes and no new completed scenarios | **none** |
| batch 5 | 17 | **recovery run active** | `recovery-metadata.json`: first sweep blocked on the same mutex failure; recovery sweep relaunched at `1/1/1`, PID `70688`, resumed execution on `support-prerendering` | **none** |

**Proven (artifact-backed):**

1. **0 of the 5 child batches produced a final `results.json` at report time.** This is the
   most important operational fact. The successful 5-skill batch in §8/§9 did produce final
   result artifacts; the overnight 93-skill continuation did not.
2. **Two primary failure modes are proven.**  
   **(a)** validator/judge persistence around `session-state\\events.jsonl` appears explicitly in
   batch 1, 2, 3, 4, and 5 logs and summaries as `Failed to append to JSONL file
   session-state\\events.jsonl: timeout while waiting for mutex to become available`; and  
   **(b)** independent judge-output reliability failures appear as repeated `contained no JSON`
   parse failures, especially on Opus-judged sweeps and Opus-heavy rejudge legs. These should be
   treated as **co-equal** root causes, not a main issue plus a side effect.
3. **Model API rate limiting is not the dominant blocker in this attempt.** The child summaries
   consistently reported mutex timeouts, `contained no JSON` judge parse failures, wrapper
   stalls, and sweep exits with no result file; they did **not** report 429/rate-limit as the
   main failure. This is a materially different failure signature than the earlier transient
   rate-limit hit in the 5-skill batch (§8.2 item 4).
4. **Waiting longer is low-confidence.** Even when a sweep keeps moving, successful artifact
   emission is unproven: batch 4 has both `exit 4294967295` **and** `exit 0` cases that still
   yielded no `results.json`.

**Inferred (strong but not directly isolated):**

- The failure appears to be **amplified by scale and shared persistence**, not by one bad skill.
  The same `events.jsonl` mutex pattern surfaced across multiple independent sessions, models,
  and skill sets.
- Running 5 child batches in parallel likely **increased contention risk**, but this is not
  proven as the sole cause: batch 4 still produced no result file even after dropping one sweep
  to `1/1/1`, so there is likely a deeper interaction in the validator/judge persistence path.
- **Important caveat:** none of the `1/1/1` recovery attempts were truly fully serialized unless
  `--no-overfitting-check` was also set. The validator launches overfitting analysis in parallel
  with scenario execution, so `1/1/1` alone still permits concurrent LLM activity.

### 12.2 Consequence for the replace decision

> **Bottom line:** the full remaining `n=5` sweep should be treated as **blocked / unreliable**.
> Continuing to wait is not the best path to decision-grade information.

The current evidence is already sufficient to answer the original decision questions better than
an open-ended wait:

1. **What does the new setup buy?** §8 and §9 already show the upside of `runs=5` + multi-model
   evaluation on a completed sample: verdicts changed, CIs matter, and cost rises ~15×.
2. **How risky is the rollout path?** This full-catalog attempt shows the operational risk plainly:
   the current validator/judge persistence path is **not yet reliable at catalog scale**.
3. **What do we need before retrying at scale?** Fix the persistence failure mode first; only then
   is a large 93-skill continuation likely to produce trustworthy artifacts.

### 12.3 Partial A/B that still completed under the pressure

The repeated rejudge A/B also degraded under scale, but it still yielded a small completed subset:

| Skill | Judge | Completed repeats | Mean score % | Range | Passes |
|---|---|---:|---:|---|---:|
| binlog-generation | sonnet-4.6 | 2 | **6.83** | 5.86 … 7.81 | 0/2 |
| minimal-api-file-upload | opus-4.8 | 2 | **−3.55** | −5.52 … −1.58 | 0/2 |
| minimal-api-file-upload | sonnet-4.6 | 2 | **6.21** | 5.33 … 7.10 | 0/2 |
| maui-theming | sonnet-4.6 | 2 | **−7.69** | −8.36 … −7.02 | 0/2 |

And the failures themselves are informative:

- **Opus rejudge** failed twice on `binlog-generation`, twice on `maui-theming`, and twice on
  `dump-collect`.
- **Sonnet rejudge** completed `binlog`, `minimal-api`, and `maui`, but one `dump-collect`
  repeat still failed after two timeouts.

That is not enough to make a strong repo-wide statistical claim, but it is enough to reinforce
the operational conclusion of this section: **the hard problem is no longer “can we start the
runs?” but “can the validator reliably persist and finish them at scale, while also getting the
judge to return parseable JSON reliably?”**

---

## 13. Alternative ways to do full validation from here

The failed overnight fan-out does **not** mean “don’t do full validation.” It means “don’t do it
again in the same shape.” Below are the practical alternatives, ordered by recommendation.

### 13.1 Recommended next attempt: sequential batches, one sweep at a time

| Strategy | Shape | Why it helps | Tradeoffs |
|---|---|---|---|
| **Sequential batches (recommended)** | 10–20 skills per batch, but run **one batch at a time** and **one executor/judge sweep at a time** | Minimizes shared persistence pressure; failures stay localized; easier restart/resume; simplest operational model | Slowest wall-clock if all goes well |
| **Fully serialized per sweep** | `--parallel-skills 1 --parallel-scenarios 1 --parallel-runs 1 --no-overfitting-check` | Lowest contention footprint; first meaningful “can this pipeline emit `results.json` at all?” canary | Very slow; still not a guaranteed fix |
| **Agent-first, judge-later (`--no-judge`)** | Run all agent arms first with `--no-judge`, persist sessions, then rejudge in tiny batches with a **non-Opus** judge (GPT or Sonnet) | Decouples expensive/explosive judge path from agent execution; judge retries become isolated and resumable | Requires extra orchestration; judge-side persistence bug may still need mitigation |
| **Tiered gate (`n=3 → n=5`)** | Use n=3 broadly; escalate only borderline / disagreement skills to n=5 | Cuts total work materially while avoiding single-draw decisions; spends `n=5` only where it changes decisions | Less pure than a full `n=5` catalog sweep |
| **Disable overfitting-check on broad sweeps** | Use `--no-overfitting-check` on catalog-wide runs; run it only on shortlisted skills | Shrinks runtime and one whole axis of work | Loses a safeguard on the broad pass |

### 13.2 Strongest path for a dependable rerun

**Best operational plan (inferred, but directly motivated by §12 evidence):**

1. **Run agent arms first with `--no-judge`**, in small sequential batches (10–20 skills).
2. **Persist sessions only** and verify every batch produced the expected session trees.
3. **Rejudge those saved sessions later**, one judge model at a time, in tiny batches, starting
   with **GPT-5.5 or Sonnet** rather than Opus.
4. **Use `n=5` only for the disagreement set / near-threshold set**; use `n=3` for
   clearly-bad / clearly-good skills.

Why this is strongest:

- It isolates the proven hot spots: the **judge/persistence** path and the **judge JSON-return**
  path.
- It preserves useful work even when judging fails.
- It makes retries cheap and local: a failed rejudge batch does not throw away the agent runs.
- It creates a natural checkpoint structure for publishing history to `skills-data`.

### 13.3 Weakest path

**Weakest option:** repeat the exact overnight shape (5 child batches in parallel, each trying
to drive all 3 sweeps to completion with judging enabled).

That path has now accumulated enough direct negative evidence that rerunning it unchanged would be
hard to justify:

- 5/5 batches hit the same persistence-class problem;
- 0/5 produced final `results.json`;
- at least one sweep exited cleanly (`exit 0`) and still emitted no result artifact.

### 13.4 Recommendation

- **For this decision cycle:** ship the report from the completed 5-skill `n=5` batch, the partial
  A/B, and the blocked full-sweep evidence.
- **For the next execution cycle:** rerun full validation as **sequential, checkpointed batches**
  with **agent-first / judge-later** separation, and reserve `n=5` for the threshold-sensitive
  subset instead of the whole catalog.
