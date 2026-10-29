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
below-threshold method represented by the report. Use both for explicit
project-wide risk work. Default output remains legacy. For an explicitly
requested uncertainty-aware result, both scripts accept `-OutputFormat json-v1`.

### Output compatibility and evidence limits

JSON v1 emits one object with `schema_version`, `tool`, `tool_version`, `status`,
`scope`, `inputs`, `diagnostics` and `data`. Inspect status and diagnostics before
using `data.summary`, `data.methods` or CRAP `data.hotspots`. Exit 0 is complete,
3 is partial and 2 is an analyzer error; host/parameter-binding failures can
occur before an envelope exists. Legacy mode refuses unrepresentable partial
results rather than inventing metrics. Do not silently change a caller's format.

- Missing method coverage or complexity stays null/unknown, not zero or one.
  Check `LineCoverageStatus`, `BranchCoverageStatus` and `ComplexityStatus`;
  unknown `CrapScore` rows remain in methods but are excluded from hotspots.
- Overlapping branch counters without stable identities produce bounds
  (`CoveredBranchesLower`/`Upper`, `BranchCoverageLower`/`Upper`), not an exact
  union. A threshold inside those bounds remains unresolved.
- Aggregate and method denominators are different. Fully covered reported
  methods do not prove that the entire module is covered. Verify overlapping
  report inputs and scope before interpreting aggregate totals.
- Input hashes in v1 bind report bytes, not source-revision attestation;
  `scope.source_revision` is null. Retain the command and known report/revision
  association separately.
  A successful exit or an empty result is not proof that no risky code exists.

Keep unsupported or missing metrics visible as unresolved evidence, not
low-risk results. Do not invent precision, silently switch output formats or
reuse an unrelated eval recorder's schema. Preserve analyzer exit codes through
outer launchers and keep partial/unknown metrics visible in the final report.

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
