# eval-review — repo-local skill/eval quality graders

Static graders (free, no model) that check the **quality of skills and their
evals** before they merge, plus an **opt-in LLM grader** for the judgment
dimensions. They encode a skill/eval review: de-leading, assertion hardening,
provenance hygiene, negative-scenario coverage (all static/free), and — behind an
explicit `--llm` flag — fixture honesty, design balance, and domain-correctness.

> **Enforcement is standalone, not via Vally.** These checks run in CI through the
> `review.mjs` runner in this package — **not** through Vally. We verified that
> `vally lint --grader-plugin` does **not** execute custom static graders against
> skills (its lint registry only holds the three built-ins; the plugin registry is
> used only to *validate* grader `type`s referenced in an eval spec), and that
> `vally experiment run` — this repo's actual eval path — has no `--grader-plugin`
> option at all. The graders are *also* exported as Vally `Grader` objects via
> `registerGraders(registry)` so they can be loaded by `vally eval`/`grade` and
> tested with Vally's APIs if the repo ever adopts those paths, but that is a
> future/compatibility layer, not the current gate. See "Vally compatibility" below.

## What it checks

| Grader | Determinism | Checklist | What it flags |
|---|---|---|---|
| `eval-deleading` | static | A | Fixture comments / eval prompts that leak the answer (e.g. *"the right fix is …"*, a fixture that names the exact edit). High-confidence leaks only; generic smells stay MINOR. |
| `assertion-hardening` | complex-static | B | A **mutating** scenario (edit/fix/migrate provided files) whose assertions are prose/`output_*`-only, with no result gate (`run_command_and_assert` / `file_contains` / `exit_success`). A wrong edit can still pass. |
| `meta-commentary` | static | D5 | Unsupported provenance/empirical asides in shipped skill text (*"learned from telemetry"*, *"grounded in N PRs"*). Never *requires* provenance. |
| `negative-scenario` | static | D4 | A real `DO NOT USE FOR` exclusion with **no** eval scenario asserting non-activation (`expect_activation: false`). |
| `eval-review` | llm | C / D / DC | **Opt-in, paid.** A real judge-model call assessing fixture honesty (C), skill-design balance (D1–D3), and domain-correctness & safety (DC). Emits the same `[SEVERITY]/PROOF/WHY/FIX` findings. Off by default and **not** in the free CI gate; run with `review.mjs --llm`. |

Findings use the review report shape:

```
[SEVERITY] dimension (rule) — file:line
  PROOF: "<exact quoted text>"
  WHY:   <why this matters>
  FIX:   <concrete fix>
```

Severities: **BLOCKER > MAJOR > MINOR > INFO**. Verdict axis: **Rework**
(any BLOCKER) / **Fix-then-ship** (any MAJOR/MINOR) / **Ship**.

### Severity philosophy

MAJOR is reserved for high-confidence, evidence-backed defects so reviewers never
learn to ignore it:

- **Evidence rule.** A MAJOR/BLOCKER with no quoted `file:line` proof is
  automatically downgraded to MINOR (`lib/report.mjs`). No proof → no failing
  severity.
- **Intent-first classification.** `assertion-hardening` classifies each scenario
  *before* demanding a result gate. It reads the scenario **name** first (the
  author's one-line intent) and only treats **base-form imperative** verbs as
  agent instructions — so *"the project generates a file"* (a description) is not
  read as *"generate a file"* (an instruction). Only **editing existing provided
  files** (fix/migrate/convert/refactor) earns MAJOR; *producing new* content
  checked by keywords is under-asserted → MINOR. Advisory/diagnostic scenarios
  (diagnose/explain/recommend, or "do not modify") are never flagged.
- **Routing-risk tiering.** `negative-scenario` is MAJOR only when the exclusion
  hands off to a **sibling skill** (`use some-other-skill`) — an untested routing
  boundary that can misroute. A generic exclusion with no negative scenario is a
  coverage gap → MINOR.

## Usage

```bash
# One-time: install the runtime deps (js-yaml, argparse). The free static gate
# only needs these; add the optional @github/copilot-sdk + zod (drop --omit) for --llm.
cd eng/vally-graders && npm ci --omit=optional

# From the repo root — evaluate only the skills touched by the PR (default):
node eng/vally-graders/review.mjs

# Evaluate everything:
node eng/vally-graders/review.mjs --all

# Focus on one skill; emit JSON:
node eng/vally-graders/review.mjs --unit dotnet-test-migration/migrate-mstest-v3-to-v4 --json

# Report-only a noisy dimension (prints, never blocks):
node eng/vally-graders/review.mjs --report-only eval-deleading --report-only negative-scenario

# Opt into the paid LLM grader (needs a Copilot token + optional deps installed):
node eng/vally-graders/review.mjs --llm --unit dotnet-test-migration/migrate-mstest-v3-to-v4
```

Key flags: `--all`, `--base <ref>` (default `GITHUB_BASE_REF` or `main`),
`--unit <substr>` (repeatable), `--report-only <dimension>` (repeatable),
`--fail-on BLOCKER|MAJOR|MINOR` (default `MAJOR`), `--repo-root <path>`, `--json`,
`--llm` (opt into the LLM grader), `--model <id>` (judge model for `--llm`).
Exit code is non-zero when a blocking finding is present. Under GitHub Actions it
also emits `::error`/`::warning` annotations.

### LLM review (opt-in, paid — `--llm`)

The `eval-review` grader makes a **real judge-model call** to assess the judgment
dimensions the static graders can't: fixture honesty (C), skill-design balance
(D1–D3), and domain-correctness & safety (DC). It is **off by default** and
deliberately **excluded from the free CI gate** because it costs money/latency.

- **Enable:** `review.mjs --llm`. Select the model with `--model <id>` or the
  `EVAL_REVIEW_MODEL` env var (default `claude-opus-4.6`, matching the repo's
  judge-model default in `eng/vally-adapter/adapt.mjs`).
- **Auth + deps:** provisions the same client Vally's CLI uses via the optional
  `@github/copilot-sdk` (+ `zod`) dependency; install with `npm ci` (i.e. **without**
  `--omit=optional`). Needs a Copilot-scoped token in `GITHUB_COPILOT_API_TOKEN`,
  `GITHUB_TOKEN`, or `GH_TOKEN`.
- **Evidence discipline / anti-fabrication:** every model finding is routed through
  the same evidence rule as the static graders — a MAJOR/BLOCKER with no quoted
  `file:line` proof is downgraded, cited files must exist in the unit, and quoted
  proof is verified against the unit's actual text (unverifiable quotes are
  dropped). The grader **never** emits a BLOCKER on error.
- **Graceful failure:** any SDK/token/model/timeout failure yields an
  **inconclusive** NOTE (not a finding) and is excluded from the verdict, so a
  missing token or offline run can never produce a false BLOCKER.
- **Vally-compat:** still registered as a `determinism: "llm"` grader
  (`experimental: true`) so `vally eval`/`grade --grader-plugin` can load it.

### Suppression

Add an inline `eval-review-ignore: <rule> — <rationale>` comment on or next to a
flagged line to suppress a specific de-leading finding when a fixture *must*
contain the smell (e.g. a fixture that deliberately models bad code).

## CI wiring

The gate runs in `.github/workflows/skill-check.yml` as **steps in the existing
`check` job** (`on: pull_request`) — no separate workflow file — as a free,
no-model check:

1. `npm ci --omit=optional` + `npm test` — validates the graders themselves
   (the paid LLM optional deps are skipped; the suite runs without them).
2. `node eng/vally-graders/review.mjs --base origin/<base> --report-only
   eval-deleading --report-only negative-scenario` — enforces the crisp
   `assertion-hardening` / `meta-commentary` MAJORs while the noisier dimensions
   surface as warnings during rollout. The paid `--llm` grader is **not** run here.

**Scope.** The PR gate runs **changed units only** (the `--base origin/<base>`
default). A **run-all** path is available for manual/nightly use via
`review.mjs --all`, exposed in CI through the `EVAL_REVIEW_ALL=1` env toggle.

**Dimension ratchet.** Tighten enforcement over time by removing `--report-only`
flags as each dimension's backlog is burned down.

> **Note — not added to the eval infra-glob.** `eng/vally-graders/` is intentionally
> *not* in the `is_infra` change globs in `evaluation.yml`. Those globs trigger a
> full model-eval smoke run, but the graders are not part of Vally's eval path, so a
> grader change would burn eval budget for zero signal. Grader changes are instead
> covered by `npm test` in the `check` job.

## Vally compatibility (future layer, not the gate)

`index.mjs` exports `registerGraders(registry)` (the `GraderPluginEntry`
contract), so `eng/vally-graders` is loadable as a local grader plugin:

```bash
vally eval  --grader-plugin ./eng/vally-graders ...
vally grade --grader-plugin ./eng/vally-graders ...
```

Custom graders execute in `eval`/`grade` (per trajectory), **not** in `lint`
against skills, and **not** in `experiment`. To reference a grader `type` from an
`eval.vally.yaml`, run via `eval`/`grade` with `--grader-plugin`.

### Version pin (important)

`--grader-plugin` availability was confirmed on the installed
`@microsoft/vally-cli@0.6.0` (present on `lint`/`eval`/`grade`/`compare`/`export`,
**absent on `experiment`**). The repo's Vally workflow installs the CLI **unpinned**
(`latest`). If the repo ever routes these graders through Vally, pin
`@microsoft/vally-cli` to a version known to support the intended flags and add a
smoke test that fails loudly if `experiment` still can't load plugins.

## Layout

```
index.mjs            # registerGraders(registry) + exports (staticModules, graders)
review.mjs           # standalone CLI gate (the real enforcement path)
graders/
  eval-deleading.mjs        # A
  assertion-hardening.mjs   # B
  meta-commentary.mjs       # D5
  negative-scenario.mjs     # D4
  eval-review.mjs           # llm — opt-in judge-model grader (C/D/DC)
lib/
  locate.mjs         # skill<->eval discovery + eval.yaml/SKILL.md parsing
  report.mjs         # finding model, [SEVERITY]/PROOF/WHY/FIX formatting, verdict
  grader-base.mjs    # wraps a { metadata, review } module as a Vally Grader
  llm-client.mjs     # @github/copilot-sdk provisioner for the --llm path
tests/
  graders.test.mjs   # node:test suite (run with `npm test`)
  fixtures/repo/      # synthetic leaky + clean skill/eval pair
```

## Design notes

- **Plain Node ESM `.mjs`**, matching the `eng/` convention (no TypeScript build).
- **`js-yaml` dependency** is a deliberate deviation from `eng/`'s dependency-free
  style — Node has no built-in YAML parser and eval specs are YAML. It (plus its
  transitive `argparse`) is the only *required* runtime dependency; `npm ci
  --omit=optional` installs it in CI. The `--llm` path additionally uses the
  **optional** `@github/copilot-sdk` + `zod` deps (installed by a plain `npm ci`),
  which the free static gate skips.
- Dimensions **3 (full fixture right-vs-wrong discrimination)** and **5 (empirical
  A/B value)** are out of scope for v1 static graders and are *not* claimed as
  covered.
