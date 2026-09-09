# Explicit Risk/CRAP Analysis

Read this file only when the user explicitly asks for project-wide risk
hotspots, CRAP scores, complexity-weighted priorities, or refactoring safety.
Do not read it for supplied-excerpt interpretation, plateau diagnosis, or target
arithmetic alone.

## Compute the risk data

Resolve the scripts relative to this skill's `SKILL.md`:

```powershell
& "<skill-directory>/scripts/Compute-CrapScores.ps1" `
    -CoberturaPath @(<all Cobertura paths>) `
    -CrapThreshold <crap_threshold> `
    -TopN <top_n>

& "<skill-directory>/scripts/Extract-MethodCoverage.ps1" `
    -CoberturaPath @(<all Cobertura paths>) `
    -CoverageThreshold <line_threshold> `
    -BranchThreshold <branch_threshold> `
    -Filter below-threshold
```

`Compute-CrapScores.ps1` emits aggregate line/branch coverage, method counts,
flagged counts, and sorted hotspots. `Extract-MethodCoverage.ps1` emits every
below-threshold method. Use both for explicit project-wide risk work.

CRAP is:

`CRAP(m) = complexity² × (1 − lineCoverage)³ + complexity`

A method at 100% coverage therefore has CRAP equal to its complexity. Use 30 as
the default flagged threshold; treat 15–30 as moderate rather than catastrophic.

## Scale the output

- Show the top 3 actual risk hotspots by default.
- Exclude fully covered low-risk methods from the hotspot table.
- State how many additional methods exceeded the threshold instead of listing
  them all.
- Honor a user-supplied count up to 10. Exceed 10 only when explicitly requested.
- For five or fewer below-threshold members, name all of them. For larger sets,
  show the top hotspots and summarize the remaining count and range.
- Give 1–3 recommendations ordered by expected risk reduction.

## Prioritize

- **HIGH** — both CRAP and coverage exceed their risk thresholds.
- **MED** — either CRAP or coverage exceeds its threshold.
- **LOW** — below coverage threshold but complexity is at most 2.

Prefer complex uncovered critical paths (authentication, payment, data access,
error handling). Deprioritize trivial getters, generated code, migrations, and
configuration glue.

Do not project a CRAP reduction from an arbitrary target without showing the
assumption. Recalculate with the stated projected method coverage.

Do not generate tests during analysis. Recommend focused test cases;
implementation is a separate follow-up.

## Style

- Lead with the risk verdict, not setup narration.
- Quantify recommendations only from actual line/coverage evidence.
- Use one compact hotspot table. Do not append the full report template unless
  the user requested a report.
