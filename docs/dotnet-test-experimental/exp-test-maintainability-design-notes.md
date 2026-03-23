# Test Maintainability Skill — Design Notes

## Evaluation Results (March 2026)

### Round 1 — Original skill

| Scenario | Baseline | Isolated | Plugin | Verdict |
| --- | --- | --- | --- | --- |
| Selectively recommend changes | 4.7/5 | 5.0/5 | 5.0/5 | ❌ ¹ |
| Data-driven patterns + display names | 4.0/5 | 5.0/5 | 4.7/5 | ✅ |
| Well-maintained recognition | 4.0/5 | 5.0/5 | 5.0/5 | ✅ |
| Oversized tests | 5.0/5 | 5.0/5 | 5.0/5 | ❌ ¹ |

¹ Quality matched or improved but weighted score penalized by token overhead.

### Round 2 — After trimming (removed Steps 4-5, pitfalls, validation checklist)

| Scenario | Baseline | Isolated | Plugin | Verdict |
| --- | --- | --- | --- | --- |
| Selectively recommend changes | 5.0/5 | 5.0/5 | 5.0/5 | ❌ ² |
| Data-driven patterns + display names | 4.0/5 | 4.6/5 | 4.4/5 | ❌ ³ |
| Well-maintained recognition | 4.6/5 | 5.0/5 | 5.0/5 | ✅ |

² Baseline at ceiling — same problem as "Oversized tests".
³ **Regression** — trimming removed implicit reinforcement about `DataRow`+`DisplayName`.
  The skill steered the model toward `[DynamicData]` instead of `[DataRow]` with
  `DisplayName`, which the rubric penalizes. Fixed by adding an explicit calibration
  rule: "Prefer `[DataRow]` with `DisplayName` over `[DynamicData]` when values are
  compile-time constants."

## Key Insight

The baseline LLM already excels at refactoring recommendations (extracting builders,
splitting oversized tests). The skill's unique value is in **judgment calls**:
recognizing well-maintained tests, calibrating when NOT to recommend changes, and
recommending display names for non-obvious values.

## Decisions

### Removed: "Oversized tests" eval scenario

Baseline scores 5.0/5 — there is zero quality delta for the skill to contribute. Any
non-zero token overhead makes the weighted score negative. This scenario cannot pass
regardless of how much we trim the skill.

### Kept: "Selectively recommend changes" eval scenario

Baseline scores 4.7/5 vs plugin 5.0/5 — there IS a +0.3 quality uplift. Trimming the
skill's token footprint may allow the quality delta to outweigh the overhead penalty.

### Trimmed: SKILL.md output formatting and pitfalls sections

Removed Steps 4-5 (detailed report structure, show-refactored-code instructions),
the Validation checklist, and the Common Pitfalls table. These either duplicate the
Step 3 calibration rules or teach behaviors the model already does natively
(before/after code, quantified benefits). This cuts ~25% of skill tokens while
preserving the core detection tables and calibration guidance that drive the passing
scenarios.

## Scenarios Not Worth Adding

These refactoring tasks were considered but not pursued as eval scenarios because the
baseline model handles them at or near ceiling quality:

- **Identifying oversized / multi-concern tests** — Model reliably spots 50+ line tests
  with multiple arrange-act-assert cycles and recommends splitting.
- **Extracting repeated setup into helpers** — Model recognizes 3+ repeated setup blocks
  and suggests `TestInitialize`, helper methods, or factory patterns.
- **Recommending builder patterns** — Model identifies scattered complex object
  construction and proposes builders when warranted.

The skill focuses instead on the judgment-heavy scenarios where the baseline struggles:
restraint (knowing when code is already good enough) and display name calibration.
