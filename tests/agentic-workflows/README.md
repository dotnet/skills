# Offline workflow decision evaluations

These suites evaluate the real packaged Markdown prompts, their local imports,
and installed agents against deterministic evidence. The lane is
`workflow-prompt-sdk`, **not gh-aw Actions E2E**: no artifact download, bootstrap
job, live GitHub/ADO/MCP tool, safe-output publisher, or consumer verification
hook runs.
The evaluator denies shell execution in all model sessions, including nested
agents, while preserving file tools for reading evidence and writing proposals.
Only `result.json` may be written; staged evidence, context, and package resources
are protected from file-tool and filesystem-provider mutations.
Trusted setup/grader commands run outside that model permission boundary.

From the repository root, select a package manifest:

```powershell
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- evaluate `
  agentic-workflows/build-failure-analysis/aw.yml `
  --tests-dir tests/agentic-workflows --runs 1 --verdict-warn-only
```

Substitute `msbuild-quality-review`, `test-failure-analysis`, or
`unskip-closed-tests`. Results identify `skillKind=workflow` and
`evaluationLane=workflow-prompt-sdk`. All arms receive the same offline
transport and result contract. The isolated arm loads the composed workflow
prompt and can read installed agent guidance; the package arm also registers
packaged agents. Runtime resources use the same installed `.github` paths as
a consumer. Only the main Markdown body and local imported Markdown bodies
are composed as model prompts; frontmatter jobs and steps are not prompts and
are not executed. The internal synthetic persona is `workflow.<package>` to
avoid collisions with bundled agents; raw/adapted target identity remains the
package name.

Collector/job gates are deliberately outside this lane. Missing-artifact,
zero-eligible-candidate, stale, and incompatible inputs exercise the proposed
decision that is safe **if presented to the prompt**; they do not prove the
live agent job would run. In production, frontmatter conditions or deterministic
collectors may stop those inputs before any model call. Compiled workflow and
native-helper evidence validate that separate bootstrap/eligibility boundary.
`expect_activation: true` here means an active offline decision case, not a
claim about live Actions job activation.

## Fixtures and grading

Every stimulus stages one complete case directory at `inputs/`, its flat
`workflow-context.json` at the workdir root, the common `RESULT_SCHEMA.md`, and
the evaluator-owned Python grader. The expression file maps exact trimmed
expression strings (without `${{`/`}}`) to string values; missing values fail
explicitly. Cases without body expressions use `{}`. The review cases supply
the initial trusted base SHA and configured exclusions; later PR reads remain
separate evidence. `inputs/context.json` supplies all exported runtime-variable
replacements and simulated collector/repository reads. Prompts ask for a
decision, not a particular package, agent, tool, or implementation technique.
Fixtures contain evidence and simulated repository state, never expected
decisions. They are small constructed, committed snapshots, not claimed
captures from a real CI run. Reported build causes have matching source;
test records support observed classifications, not speculative code causes.

Expected actions, input digests, outcome patterns, classification bounds, and
candidate sets are passed only as argv by each evaluator-only `run-command`
in `eval.yaml`. Neither the specs nor regression tests are staged for the
agent. The staged script contains generic checks only, with no per-case
expected-answer maps or fixture-derived inference of the expected action.
The grader validates strict JSON, proposed-only semantics, citations,
changed-line locations, selection identities, scenario outcomes, and the
canonical SHA-256 of the entire input tree. Its own SHA-256 is pinned in each
run-command assertion to prevent self-modification. Prompt rubrics judge
decision quality and explanation rather than workflow terminology. No-op
cases are preference-eligible, not dormancy guards.

Input-tree digests sort case-sensitive, POSIX-style relative paths and normalize
CRLF content to LF before hashing. This keeps the authenticated fixture identity
the same on Windows and Linux without relaxing source/evidence tamper checks.

Completion is established by the result artifact and generic structured
grader, not `exit-success`: recoverable SDK tool errors are diagnostics rather
than terminal failure evidence. Equivalent contained citation paths with or
without the `inputs/` prefix are accepted. A no-op/selection may omit its body
or serialize it as null/empty, but any visible body still fails.
Line citations must use `line N`. Filename-like IDs such as `records.jsonl:1`
are accepted only when that exact value occurs in the cited JSON; a filename
prefix cannot be discarded to invent a matching line citation.
Retry assessment grades the observed recovery in each finding summary, not
whether a generic classification label uses the word “failure” or “flake”.
Selection requests may carry cited explanatory findings; exact candidate
identities, eligibility, source revision/digest, and non-authorization remain
mandatory. Incorrect comment/review/no-op actions and malformed JSON still
fail rather than being normalized into a desired decision.

Coverage:

| Suite | Cases | Distinct boundaries |
| --- | ---: | --- |
| Build evidence | 10 | Cross-leg cascades, warning promotion, non-build failure, missing leg, no logs, silent process failure, head movement, merge movement, package availability uncertainty, partial useful evidence. |
| Project review | 10 | Extension chains, default items, generated outputs, valid packed imports, F# ordering, exclusions, base movement, missing full file, mixed scope, early property evaluation. |
| Test evidence | 12 | Grouped terminal failures, retry recovery, watchdog timeout, process crash, duration thresholds, incomplete categories/history, lifecycle ordering, forged lifecycle marker, inconclusive final, empty complete bundle, empty final replacement, stale merge. |
| Ignored-test selection | 11 | Completed issue, merged PR, unresolved/not-planned items, unrelated issue context, complete class ownership, partial/nested class, wrong method owner, stale revision, multiple modules, zero execution evidence, incompatible manifest. |

The ignored-test suite checks manifest-based selection/deferral proposals and
the distinction between proposals and verification. It does **not** execute
the native collector/apply/authorize/materialize helper, remove attributes,
validate real TRX output, or prove a draft PR can be published. Existing
package helper tests remain the separate execution/protocol coverage.

## Deterministic validation (no model calls)

```powershell
python -m unittest discover -s tests/agentic-workflows -p test_*.py -v
git add -- tests/agentic-workflows
python eng/eval-quality/check_eval_quality.py
```

`python tests/agentic-workflows/make_unskip_fixtures.py --check` verifies the
generated simulated manifests against their recorded source; omit `--check`
to regenerate them after an intentional fixture change. Similarly,
`python tests/agentic-workflows/make_workflow_contexts.py --check` checks flat
expression contexts and runtime variables. Regenerate manifests first, then
workflow contexts when changing ignored-test evidence. Update evaluator-only
`--input-digest` arguments when changing inputs and the authenticated command
pins in all four specs when changing the generic grader. JSON source/evidence
paths use portable `/` separators.
Drive-relative paths, rooted paths, and colon/alternate-stream forms are rejected
before any evidence-file lookup on both Windows and Linux.

The regression suite materializes each case under this test directory and
cleans up afterward. It exercises correct, wrong-action, spurious-noop,
malformed, fabricated-citation, changed-source, redirected-selection, and
unsupported-publication results. Full paired model trials are a separate
operation; deterministic passes alone establish no preference verdict.
