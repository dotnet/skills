---
name: coverage-analysis
description: >
  Project-wide code coverage and CRAP (Change Risk Anti-Patterns) score
  analysis for .NET projects. Calculates CRAP scores per method and surfaces
  risk hotspots — complex code with low coverage that is dangerous to modify.
  Use to diagnose why coverage is stuck or plateaued, identify what methods
  block improvement, or get project-wide coverage analysis with risk ranking.
  USE FOR: coverage stuck, coverage plateau, can't increase coverage, what's
  blocking coverage, coverage gap, CRAP scores, risk hotspots, where to add
  tests, coverage analysis, coverage report.
  DO NOT USE FOR: targeted single-method CRAP analysis (use crap-score);
  auditing test code for coverage-touching or other anti-patterns (use
  test-anti-patterns); writing tests; running tests (use run-tests). Requires
  or produces coverage (Cobertura) and CRAP metrics.
license: MIT
---

# Coverage Analysis

## Purpose

Raw coverage percentages answer "what code was executed?" — they don't answer what you actually need to know:

- **What tests should I write next?** — ranked by risk and impact
- **Which uncovered code is risky vs. trivial?** — CRAP scores separate the two
- **Why has coverage plateaued?** — identify the files blocking further gains
- **Is this code safe to refactor?** — complex + uncovered = dangerous to change

This skill bridges that gap: from a bare .NET solution to a prioritized risk hotspot list, with no manual tool configuration required.

## When to Use

Use this skill when the user mentions test coverage, coverage gaps, code risk, CRAP scores, where to add tests, why coverage plateaued, or wants to know which code is safest to refactor — even if they don't explicitly say "coverage analysis".

## When Not to Use

- **Targeted single-method CRAP analysis** — use the `crap-score` skill instead
- **Writing or generating tests** — this skill identifies where tests are needed, not write them
- **General test execution** unrelated to coverage or CRAP analysis
- **Coverage reporting without CRAP context** — use `dotnet test` with coverage collection directly

## Inputs

| Input | Required | Default | Description |
|-------|----------|---------|-------------|
| Project/solution path | No | Current directory | Path to the .NET solution or project |
| Line coverage threshold | No | 80% | Minimum acceptable line coverage |
| Branch coverage threshold | No | 70% | Minimum acceptable branch coverage |
| CRAP threshold | No | 30 | Maximum acceptable CRAP score before flagging |
| Top N hotspots | No | 10 | Number of risk hotspots to surface |

### Prerequisites

- .NET SDK installed (`dotnet` on PATH)
- At least one test project referencing the production code (xUnit, NUnit, or MSTest) — only required for the from-scratch path; not needed when the user supplies an existing Cobertura XML
- **Optional, only for the from-scratch path:** internet/NuGet access for `dotnet add package coverlet.collector` (or `Microsoft.Testing.Extensions.CodeCoverage`) when a test project has no coverage provider yet. Skip when the user supplies an existing Cobertura XML.
- **Optional, only for Phase 5:** internet access for `dotnet tool install` (ReportGenerator). Core CRAP/coverage analysis works from Cobertura XML alone — ReportGenerator only adds HTML/CSV reports as an optional post-summary extra.

The skill auto-detects coverage provider state per test project and selects the least-invasive execution strategy:

- unified Microsoft CodeCoverage when all projects use it,
- unified Coverlet when no project uses Microsoft CodeCoverage,
- per-project provider execution when the solution is truly mixed.

No pre-existing runsettings files or manually installed tools required.

## Workflow

> **MANDATORY: deliver the final assistant response with the CRAP/risk-hotspot summary BEFORE any optional work.** As soon as `Compute-CrapScores.ps1` and `Extract-MethodCoverage.ps1` return data, your **next** assistant response must contain the user-facing analysis (CRAP table, blocking methods, recommendations). Do not run ReportGenerator (Phase 5), do not install global tools, and do not start any heavy parallel work before that response is delivered. The user is judged on the final assistant message, not on side-effect files.
>
> If a phase fails, times out, or budget is running low, skip remaining optional work and immediately return a partial summary containing: (1) what was found in the Cobertura XML, (2) any CRAP/risk-hotspot data already extracted, (3) which methods are blocking coverage, and (4) failures encountered.

If the user provides a path to existing Cobertura XML (or coverage data is already present in `TestResults/`), **skip Phase 2 entirely** (no test execution) **and skip Phase 5 by default** (no ReportGenerator install or HTML report) — go directly from Phase 3 (analysis scripts) to Phase 4 (user-facing summary). Only run Phase 5 if the user explicitly asks for HTML/CSV reports. The Risk Hotspots table and CRAP scores are mandatory in every output — they are the skill's core value-add over raw coverage numbers.

The workflow runs in five phases. Phases 1–4 are required; Phase 5 (ReportGenerator HTML/CSV reports) is strictly optional and runs **after** the user-facing summary has been delivered. Do not parallelize Phase 5 with earlier phases — the heavy `dotnet tool install` for ReportGenerator can crash the session before Phase 4 completes.

### Phase 1 — Setup (sequential)

Read `references/setup-discovery.md` and run the probes it contains, in order:

| Step | Emits | Why it matters |
|------|-------|----------------|
| 1. Locate the solution or project | `ENTRY_TYPE`, `ENTRY`, `TEST_PROJECTS`, `TEST_OUTPUT_ROOT` | Entry point for `dotnet test` and where skill outputs are written |
| 2. Create the output directory | `COVERAGE_DIR` | Skill-owned `TestResults/coverage-analysis/`; never deletes user-supplied reports |
| 2b. Discover or accept existing Cobertura XML | `EXISTING_COBERTURA_COUNT`, `EXISTING_COBERTURA` | A user-supplied path always wins; otherwise probe `TestResults/` |
| 2c. Recommend ignoring `TestResults/` | `GITIGNORE_RECOMMENDATION` | One-line recommendation, reported in the summary |

Branching after Phase 1:

- `EXISTING_COBERTURA_COUNT` > 0 → **skip Phase 2 entirely**; go to Phase 3 with those paths. Do not read `references/test-execution.md`.
- `EXISTING_COBERTURA_COUNT` == 0 and test projects were found → run Phase 2.
- `ENTRY_TYPE:NotFound` with test projects → use the test projects directly as entry points.
- No test projects and no Cobertura XML → stop: `No test projects found (expected projects with 'Test' or 'Spec' in the name), and no existing Cobertura XML was provided. Add a test project or provide a Cobertura file path.`

### Phase 2 — Test execution (skip when Cobertura XML already exists)

Run only when Phase 1 found no Cobertura XML. If the user already has coverage data, skip directly to Phase 3 — do not read this section's reference file, and do not re-run the suite.

Read `references/test-execution.md`. It covers provider detection (`Microsoft.Testing.Extensions.CodeCoverage` vs `coverlet.collector`), adding a provider to projects that have none, the `dotnet test` command for each provider and SDK version, mixed-provider solutions, exit-code handling, and locating the generated reports.

Exit codes: **0** all passed; **1** some tests failed (coverage is still collected — proceed with a warning); anything else is a build failure — stop and report it.

### Phase 3 — Analysis (sequential)

Run the two bundled PowerShell scripts. Both are cheap and complete in seconds. **Do not** install or invoke ReportGenerator here — that belongs in optional Phase 5, after the user-facing summary has been delivered.

#### Step 4: Calculate CRAP scores using the bundled script

Run `scripts/Compute-CrapScores.ps1` (co-located with this SKILL.md). It reads all Cobertura XML files, applies `CRAP(m) = comp² × (1 − cov)³ + comp` per method, and returns the top-N hotspots as JSON.

To locate the script: find the directory containing this skill's `SKILL.md` file (the skill loader provides this context), then resolve `scripts/Compute-CrapScores.ps1` relative to it. If the script path cannot be determined, calculate CRAP scores inline using the formula below.

```powershell
& "<skill-directory>/scripts/Compute-CrapScores.ps1" `
    -CoberturaPath @(<all COBERTURA file paths as array>) `
    -CrapThreshold <crap_threshold> `
    -TopN <top_n>
```

Script outputs: `OVERALL_LINE_COVERAGE:<n>`, `OVERALL_BRANCH_COVERAGE:<n>` (aggregated project-wide rates across all provided Cobertura files), `TOTAL_METHODS:<n>`, `FLAGGED_METHODS:<n>`, `HOTSPOTS:<json>` (top-N sorted by CrapScore descending). The OVERALL_* values are exactly what the Phase 4 summary needs for the "Line Coverage" / "Branch Coverage" rows — no separate XML parsing tool call is required.

#### Step 5: Extract per-method coverage gaps

Run `scripts/Extract-MethodCoverage.ps1` to get per-method coverage data for the Coverage Gaps table:

```powershell
& "<skill-directory>/scripts/Extract-MethodCoverage.ps1" `
    -CoberturaPath @(<all COBERTURA file paths as array>) `
    -CoverageThreshold <line_threshold> `
    -BranchThreshold <branch_threshold> `
    -Filter below-threshold
```

Script outputs: JSON array of methods below the coverage threshold, sorted by coverage ascending. Use this data to populate the Coverage Gaps by File table in the report.

### Phase 4 — User-facing summary (MANDATORY — your next assistant response)

As soon as Phase 3 completes, **your immediately next assistant response must contain the user-facing analysis** — do not interleave any other tool calls before it. This is the response the user (and any judge) sees. Skipping or deferring this in favor of Phase 5 (ReportGenerator) is a hard failure.

The response must include, at minimum:

0. **A direct answer to the question that was actually asked, in the first 2–4 sentences.** For "why is my coverage stuck?" / "what's blocking me?", name the blocking members and the lines involved before any table. The standard sections below still follow.
1. Overall line and branch coverage — read directly from the `OVERALL_LINE_COVERAGE:` / `OVERALL_BRANCH_COVERAGE:` lines emitted by `Compute-CrapScores.ps1` (no extra Cobertura parsing required)
2. The Risk Hotspots table built from `Compute-CrapScores.ps1` `HOTSPOTS:` output (CRAP scores, complexity, coverage)
3. Identification of the highest-risk method(s) and what is blocking coverage
4. 1–3 prioritized, specific recommendations (which method to test, expected CRAP/coverage impact)

**Every number must come from the script output, and the arithmetic must reconcile.** Uncovered lines attributed to individual members must not exceed the project's total uncovered lines, and the coverage you project after a recommendation must follow from those counts.

**List every member below threshold, not just the worst one.** `Extract-MethodCoverage.ps1` returns the full below-threshold set: name the others even if briefly. Only say "the rest is fine / leave it alone" when that set is otherwise empty — claiming one method is the entire gap when the extractor found more is a factual error.

Use `references/output-format.md` verbatim for fixed headings, table structures, symbols, and emoji. Use `references/guidelines.md` for prioritization rules and style.

If Phase 5 has not yet run when you compose this summary, mark the `## 📁 Reports` section's HTML/Text/CSV/GitHub-markdown rows as `Not generated (optional — request HTML reports to enable)`. Only the `coverage-analysis.md` and raw Cobertura paths are guaranteed to exist.

Attempt to save the same content to `TestResults/coverage-analysis/coverage-analysis.md` before delivering the response (use the editor's create/edit tool — do not shell out). If the file write fails, still deliver the summary and note the file-write failure explicitly.

### Phase 5 — Optional: ReportGenerator HTML/CSV reports (post-summary)

Phase 5 is **strictly optional** and runs **only after** Phase 4 has been delivered. Skip Phase 5 entirely when:

- The user supplied existing Cobertura XML and only asked for analysis (the default for the existing-data path).
- The user is diagnosing a coverage plateau or asking "what's blocking me?" — they want the answer, not a static-site report.
- ReportGenerator is not already installed and you have no clear signal the user wants HTML reports.

Run Phase 5 only when the user explicitly asks for HTML/CSV reports, or when the project flow requires them (e.g., a CI artifact upload step).

Read `references/report-generation.md` for the ReportGenerator install and invocation. It is the only heavy step in this skill: a `dotnet tool install` that can exhaust the session budget, which is why it never runs before the Phase 4 summary has been delivered.

If the install fails (no internet), leave the existing Phase 4 summary as the final output and note that HTML reports were skipped. Do not retry or block on it.

## Validation

- Verify that at least one `coverage.cobertura.xml` file was generated after `dotnet test` (or already exists when the user supplied one)
- Confirm the assistant response contained the CRAP/risk-hotspot table — saving the markdown file is secondary
- Confirm `TestResults/coverage-analysis/coverage-analysis.md` was written and contains data
- Spot-check one method's CRAP score: `comp² × (1 − cov)³ + comp` — a method with 100% coverage should have CRAP = complexity
- If Phase 5 ran, verify `TestResults/coverage-analysis/reports/index.html` exists; otherwise the report file should mark HTML/Text/CSV rows as `Not generated`

## Common Pitfalls

- **No Cobertura XML generated** — the test project may lack a coverage provider. The skill auto-adds one, but if `dotnet add package` fails (offline/proxy), coverage collection silently produces nothing. Check for `.coverage` binary files as a fallback indicator.
- **Test failures (exit code 1)** — coverage is still collected from passing tests. Do not abort; proceed with partial data and note the failures in the summary.
- **Premature end before user-facing summary** — never start Phase 5 (ReportGenerator install/run) before the Phase 4 assistant response is delivered. The heavy `dotnet tool install` can crash the session or exhaust budget, leaving the user with no analysis even though the CRAP scores were already computed.
- **ReportGenerator install failure** — if `dotnet tool install` fails (no internet) during Phase 5, leave the existing Phase 4 summary as the final output and note that HTML reports were skipped. Do not retry or block on the install.
- **Method name mismatches in Cobertura** — async methods, lambdas, and local functions may have compiler-generated names. The scripts use the Cobertura method name/signature directly; verify against source if results look unexpected.
- **Mixed coverage providers** — when a solution contains both Coverlet and Microsoft CodeCoverage projects, the skill runs per-project to avoid dual-provider conflicts. This is slower but correct.
- **Numbers that don't reconcile** — per-member uncovered lines that exceed the project total, or a projected coverage figure that doesn't follow from the counts, make the whole analysis untrustworthy. Re-read the script output rather than estimating.
- **Declaring one method "the entire gap"** — check the full below-threshold list from `Extract-MethodCoverage.ps1` first; naming a single blocker while other uncovered members exist misdirects the user's next test.
