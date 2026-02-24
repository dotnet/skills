# Eval Results — analyzing-dotnet-performance

**Model:** claude-opus-4.6 | **Runs:** 3 (parallel) | **Judge:** pairwise

| # | Scenario | Improvement | Task Comp | Quality | Judgment | Verdict |
|---|----------|-------------|-----------|---------|----------|---------|
| 1 | Regex startup budget & chain allocations | 78% | 1 | 1 | 1 | ✅ Strong |
| 2 | CurrentCulture comparer & compiled regex | 75% | 1 | 1 | 1 | ✅ Strong |
| 3 | Per-call Dictionary not hoisted to static | 75% | 1 | 1 | 1 | ✅ Strong |
| 4 | Compound allocations in recursive converter | 75% | 1 | 1 | 1 | ✅ Strong |
| 5 | StringComparison.Ordinal & FrozenDictionary | 67% | 1 | 0.8 | 1 | ✅ Strong |
| 6 | Aggregate+Replace & struct IEquatable | 70% | 1 | 0.9 | 1 | ✅ Strong |
| 7 | Branched Replace chain | 75% | 1 | 1 | 1 | ✅ Strong |
| 8 | LINQ on hot-path & char.IsUpper | 75% | 1 | 1 | 1 | ✅ Strong |
| 9 | LINQ pipeline in TimeSpan formatting | 71% | 1 | 0.9 | 1 | ✅ Strong |
| 10 | Span inconsistencies & truncation | 75% | 1 | 1 | 1 | ✅ Strong |
| 11 | Unsealed leaf classes & locale hierarchy | 75% | 1 | 1 | 1 | ✅ Strong |

**Overall: 73.7% improvement — PASSED ✅**

11/11 strong, 0 moderate, 0 weak, 0 regressed

## Progress across 4 eval runs

| Run | Overall | Strong | Changes |
|-----|---------|--------|---------|
| Run 1 (baseline) | 61.8% | 8/10 | Original eval, all 120s |
| Run 2 (timeout v1) | 61.9% | 8/11 | Scenarios 7,10 → 180s, split 10/11 |
| Run 3 (timeout v2) | 70.3% | 9/11 | Scenarios 4,8,9 → 180s |
| **Run 4 (tuned SKILL.md)** | **73.7%** | **11/11** | Inline recipes, skip file search, read-first |
