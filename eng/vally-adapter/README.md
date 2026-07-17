# Vally shadow evaluation adapter

`adapt.mjs` converts a Vally experiment's baseline and skilled `results.jsonl`
files into per-skill `results.json` verdicts for the shadow-evaluation
workflow. `run-vally-evals.sh` runs the experiment and then invokes the
adapter.

## Reliability signals

The adapter keeps infrastructure reliability distinct from a skill-quality
result:

- `erroredCount` counts matched baseline/treatment trials whose comparison judge
  failed. The adapter retries a comparison once because model or transport
  timeouts can be transient. If the retry does not reduce errors, the original
  report remains visible.
- `unmatchedBaseline` and `unmatchedTreatment` list trajectories that could not
  be paired, commonly because one arm timed out or failed before grading.
  `unmatchedTrialCount` is their combined count.
- `conclusive` is `false` when either signal is nonzero. An inconclusive verdict
  cannot pass and is rendered as a workflow warning rather than as evidence
  that a skill regressed.

Inspect the raw variant `results.jsonl` before changing a skill. A
`status: "error"` agent timeout is different from a successful pair whose
comparison evidence says `Comparison judge failed`. Do not add fixture setup,
SDK pinning, or skill instructions unless the failure occurred during that
phase.
