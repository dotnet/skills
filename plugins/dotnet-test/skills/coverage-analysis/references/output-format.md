# Output Format

Copy the template below **verbatim** for all fixed elements (headings, table headers, emoji, symbols). Only replace `<placeholder>` values with actual data. Do not substitute emoji with text equivalents, do not change `·` to `-`, do not change `×` to `x`, and do not drop section emoji prefixes.

```markdown
# Coverage Analysis - <ProjectName>

| Metric | Value |
|--------|-------|
| **Date** | <YYYY-MM-DD> |
| **Line Coverage** | <N>% |
| **Branch Coverage** | <N>% |
| **Risk Hotspots** | <N> (CRAP > <crap_threshold>) |
| **Tests** | <N> passed · <N> failed |

## Summary

| Metric | Value | Threshold | Status |
|--------|-------|-----------|--------|
| **Line Coverage** | <N>% | <line_threshold>% | ✅ / ❌ |
| **Branch Coverage** | <N>% | <branch_threshold>% | ✅ / ❌ |
| **Methods Analyzed** | <N> | — | — |
| **Risk Hotspots** | <N> | 0 | ✅ / ⚠️ |
| **Test Result** | <Passed / N tests failed> | — | ✅ / ⚠️ |

> Coverage collected from **<N> of <M> test project(s)**.
> Reports saved to: `<coverageDir>/reports/`

If any coverage provider package was added to test projects, include this note after the summary:

> ℹ️ **Coverage provider package updates**
> - `coverlet.collector` added to `<K>` project(s): `<TestProject1.csproj>`, `<TestProject2.csproj>`
> - `Microsoft.Testing.Extensions.CodeCoverage` added to `<M>` project(s): `<TestProject3.csproj>`
>
> To revert: `git checkout -- <path-to-each-modified-csproj>`

If all test projects already had a coverage provider, omit this note.

---

## 🔥 Risk Hotspots (Top <N> by CRAP Score)

Methods flagged as high-risk: complex code with low test coverage that is dangerous to change.

| Rank | Method | Class | File | Complexity | Coverage | CRAP Score |
|------|--------|-------|------|-----------|---------|-----------|
| 1 | `<method>` | `<class>` | `<file>` | <N> | <N>% | **<score>** |
| … | … | … | … | … | … | … |

> **CRAP Score** = `Complexity² × (1 − Coverage)³ + Complexity`.
> Scores above <crap_threshold> are flagged. A score ≤ 5 is considered safe.

---

## 📋 Coverage Gaps by File

Files below the line or branch coverage threshold, ordered by uncovered lines descending:

| File | Line Coverage | Branch Coverage | Uncovered Lines | Priority |
|------|--------------|----------------|----------------|---------|
| `<file>` | <N>% | <N>% | <N> | 🔴 HIGH / 🟡 MED / 🟢 LOW |
| … | … | … | … | … |

---

## 💡 Recommendations

1. **Write tests for the top risk hotspot first** — `<method>` in `<class>` has a CRAP score of <N> (complexity <N>, <N>% coverage). Reducing it to 80% coverage would drop the score to ~<projected>.
2. **Focus on `<file>`** — <N> uncovered lines, below threshold. <Brief reasoning.>
3. **<Up to 5 actionable items total, ordered by expected risk reduction.>**

---

## 📁 Reports

| Report | Path |
|--------|------|
| HTML (browsable) | `<coverageDir>/reports/index.html` |
| Text summary | `<coverageDir>/reports/Summary.txt` |
| GitHub markdown | `<coverageDir>/reports/SummaryGithub.md` |
| CSV data | `<coverageDir>/reports/Summary.csv` |
| Raw data | `<coverageDir>/raw/` |
```
