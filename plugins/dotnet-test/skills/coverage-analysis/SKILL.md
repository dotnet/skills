---
name: coverage-analysis
description: >
  Interprets .NET Cobertura line, branch, and condition evidence and, when
  explicitly requested, computes project-wide CRAP/refactoring-risk hotspots.
  MUST USE for "why is branch coverage lower than line coverage?",
  condition-coverage="50% (1/2)", a supplied coverage excerpt, partially covered
  conditions, coverage plateaus, members blocking a target, project-wide CRAP,
  refactoring safety, or coverage-backed risk priorities. A supplied report is
  analyzed directly without rerunning tests, installing tools, generating a
  report, or calculating CRAP unless the request asks for risk/CRAP/refactoring
  safety. DO NOT USE FOR: CRAP or refactoring-safety analysis of only one named
  method, class, or file (crap-score); test trait distributions (test-tagging);
  static source-to-test pairing (find-untested-sources);
  behavioral/pseudo-mutation gaps (test-gap-analysis); test-code audits
  (test-anti-patterns); raw collection/percentage-only requests or just running
  tests (run-tests); non-.NET coverage; or writing tests.
license: MIT
---

# Coverage Analysis

## Purpose

Explain what .NET coverage evidence proves, reconcile target arithmetic, and
identify the code blocking progress. Add complexity/CRAP ranking only when the
user explicitly asks for risk hotspots, CRAP, priorities by risk, or refactoring
safety.

## When to Use

Use this skill for interpreting supplied .NET line/branch/condition evidence,
coverage gaps and plateaus, target arithmetic, or explicit project-wide
coverage-backed risk analysis.

## When Not to Use

- **Named method, class, or file CRAP/refactoring-safety analysis** — use the `crap-score` skill instead
- **Static source-to-test pairing or listing files with no tests** — use `find-untested-sources`
- **Behavioral or pseudo-mutation gaps in existing tests** — use `test-gap-analysis`
- **Test trait/category distributions or coverage shape by test type** — use `test-tagging`
- **Writing or generating tests** — this skill identifies where tests are needed, not write them
- **General test execution** unrelated to coverage or CRAP analysis
- **Only collecting .NET coverage or printing a raw percentage with no diagnosis** — use `run-tests`; use native tooling for non-.NET coverage collection or analysis. Interpreting .NET line/branch gaps remains in scope here

## Inputs

| Input | Required | Default | Description |
|-------|----------|---------|-------------|
| Project/solution path | No | Current directory | Path to the .NET solution or project |
| Line coverage threshold | No | 80% | Minimum acceptable line coverage |
| Branch coverage threshold | No | 70% | Minimum acceptable branch coverage |
| Existing Cobertura path | No | Discover only if not supplied | Preferred input; never rerun tests when usable |
| CRAP threshold | No | 30 | Used only for explicit risk/CRAP requests |
| Hotspot count | No | 3 | Explicit risk requests only; cap at 10 unless the user asks for more |

Discover optional inputs from the workspace. Do not ask for a project path when
the current directory or a supplied report is sufficient.

## Choose the smallest matching path

| User intent | Required work | Do not do |
|-------------|---------------|-----------|
| Explain a supplied excerpt, condition, or summary | Answer directly from the supplied evidence | Tools, CRAP, discovery, report files |
| Interpret a supplied Cobertura path or diagnose a plateau | Read that report, reconcile totals, name all material gaps, answer directly | Rerun tests, install tools, compute CRAP, or generate files unless explicitly requested |
| Rank risk hotspots, compute project-wide CRAP, or assess refactoring safety | Use the supplied/existing report, read `references/guidelines.md`, and compute CRAP before ranking | Coverage-only ranking or a full report template unless requested |
| Analyze coverage when no report exists | Read `references/setup-discovery.md`; collect once using `references/test-execution.md` if safe | CRAP unless risk was requested |
| Produce a full markdown/HTML/CSV report | First deliver the direct answer; then read `references/output-format.md` or `references/report-generation.md` | Report generation before the answer |

Words such as **analyze coverage**, **what is blocking coverage**, or **why is
coverage stuck** do not by themselves request CRAP. Explicit signals include
**risk hotspot**, **CRAP**, **complexity-weighted priority**, **safe to refactor**,
or an equivalent request to combine complexity with coverage.

## Existing-data fast path

When the user supplies a coverage excerpt, summary, or valid Cobertura path:

- Treat it as authoritative input and start there.
- Do not discover the solution or test projects unless source mapping is necessary.
- Do not run `dotnet test`, install ReportGenerator, add a coverage package, or
  read `references/setup-discovery.md`, `references/test-execution.md`, or
  `references/report-generation.md`.
- Do not write `coverage-analysis.md` or create a report directory unless the user
  requested a saved/full report.
- For interpretation and plateau questions, parse only the evidence needed to
  answer. For explicit project-wide risk requests, use the bundled scripts as
  described in `references/guidelines.md`.

A failed read/view operation is not proof that a named path does not exist. After
one fails, make one allowed targeted existence probe, such as a workspace-relative
glob, and retry the same artifact with a normalized path or alternate reader.
Report the exact missing-path problem only when that independent check also fails.
Do not broaden the search to unrelated coverage files or present a substitute
artifact.

## Collection path

Use this path only when no usable coverage evidence exists and the user asked for
analysis that requires it.

1. Read `references/setup-discovery.md`.
2. Prefer existing Cobertura discovered under the requested root.
3. If none exists, read `references/test-execution.md` and run the selected
   coverage command once per entry point.
4. Analyze the resulting Cobertura. Compute CRAP only if risk analysis was
   explicitly requested.

Do not modify production code. The only permitted incidental project change is
adding one missing coverage provider to an SDK-style test project as described in
`references/test-execution.md`; never add a second provider, and report the
change plus its revert command.

The automatic collection path is for SDK-style projects. For classic non-SDK or
`packages.config` projects, use only a repository-owned coverage command. If none
exists, stop and request Cobertura XML. Never migrate the project, inject an
SDK-style provider, create a wrapper project, or report substitute coverage from
another assembly.

## Interpretation and arithmetic invariants

- A line hit proves execution, not both decision outcomes.
- `condition-coverage="50% (1/2)"` proves one reported outcome ran, but not which
  one. Recommend forcing the opposite outcome. Without source, never invent likely
  predicates or claim whether true or false is missing. State that compound
  predicates need independently exercised operands and short-circuit combinations
  when applicable; do not infer the exact combinations without source or fuller XML.
- Derive overall totals from Cobertura's covered/valid line counts. For target
  `T`, required covered lines are `ceiling(valid lines × T)`.
- Projected coverage is `(current covered lines + newly covered distinct lines) /
  valid lines`. State assumptions such as fully covering a method.
- When asked whether one member can reach a target, show its maximum projected
  total and at least one concrete sufficient combination of supplied members or
  line gains. If no supplied combination is sufficient, say so.
- Reconcile member gaps against project totals. Method line ranges can overlap or
  omit class-level lines, so do not sum method counts as project truth.
- Never call one member the **sole**, **entire**, or **all** remaining gap unless
  its distinct uncovered lines exactly reconcile with the project total and no
  other below-threshold member remains.
- Name every supplied or extracted below-threshold member, but keep detail
  proportional: lead with the blockers, summarize the remainder in one sentence
  or a compact table.

## Response contract

Answer the user's question in the first 2–4 sentences.

- **Excerpt or arithmetic question:** one explanation plus the next test or member
  priority. No dashboard.
- **Existing-report interpretation or plateau:** overall line/branch coverage,
  blocking members, reconciled target impact, and 1–3 recommendations. Use at
  most one compact table.
- **Explicit risk/CRAP request:** top 3 actual hotspots by default, supporting
  complexity/coverage/CRAP values, remaining flagged count, and 1–3 priorities.
  Exclude fully covered low-risk methods from the hotspot table. Never exceed 10
  rows unless the user requests a larger count. For refactoring safety, rank risky
  methods by CRAP rather than raw coverage alone, then name comparatively safe
  well-covered methods separately.
- **Explicit full report request:** read `references/output-format.md`. Save the
  report only then.

Report only artifacts that exist. Do not announce inaccessible output paths or
failed optional file writes when no file was requested.

## Optional reports

HTML/CSV/markdown files are not part of normal analysis. Generate them only when
the user explicitly requests report files or a CI artifact.

1. Deliver the direct coverage/risk answer first.
2. For a full markdown report, read `references/output-format.md` and save it.
3. For HTML/CSV, then read `references/report-generation.md`. Do not install
   ReportGenerator before the direct answer, and do not retry a failed install.

## Validation

- Confirm a supplied report was used without test execution or tool installation.
- Reconcile covered, valid, and uncovered line totals before projecting impact.
- Confirm every stated blocker comes from supplied or extracted evidence.
- If CRAP was requested, spot-check one score using `references/guidelines.md`.
- If files were requested, verify they exist before reporting their paths.

## Common Pitfalls

- **Existing report triggers setup work** — stop. Analyze the named artifact first.
- **Generic analysis triggers CRAP** — stop. CRAP requires explicit risk intent.
- **One method is called the entire gap** — reconcile distinct uncovered lines and
  the full below-threshold set first.
- **A small request gets a dashboard** — scale down to a direct answer or one table.
- **No Cobertura from collection** — report the collection failure; do not invent
  substitute coverage.
- **Compiler-generated method names** — verify suspicious async, lambda, or local
  function names against source before presenting them as user-authored members.
- **Test exit code 1** — coverage may still exist; proceed with a warning. Other
  nonzero build failures stop the collection path.
