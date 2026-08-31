# Evaluation triage catalogue

Symptom → cause → fix, with the PR where each was diagnosed. Use with
`SKILL.md` Step 2; this file is the detail behind each cause class.

## Harness and reliability

| Symptom | Cause | Fix | Evidence |
|---------|-------|-----|----------|
| "Evaluation ran but produced no results", advice says transient infrastructure | Spec declares both `config:` and `defaults:`; vally throws, the job still exits 0 | Merge into one `defaults:` block carrying `timeout` and `runs` | PR #971 |
| Same message, nothing in `plugins/` changed | Genuine LLM-session auth failure | Re-post `/evaluate`; inspect job logs before touching content | PR #932 |
| Trial errored, avgN unusually low | Judge-side CAPI / `session.idle` timeout, not fixture nondeterminism | Read the trial stderr first; fix the harness, do not pin an SDK | PR #907 |
| Timeout on an advisory question | `expect_tools: [bash]` forced a restore or build | Drop the tool requirement; the answer was always textual | PR #861 |
| Every grader fails and output is empty | Code-generation stimulus timed out | Raise to ~360s | PR #862, PR #863 |
| Only the skilled arm aborts with "contains no SKILL.md" | A setup cleanup command deleted the staged skill directory | Skip directories carrying `SKILL.md` when stripping sources | PR #878 |
| Trials silently dropped | Setup command exited non-zero although its artifact was produced | Guard intentional failures, e.g. `dotnet build -bl \|\| exit 0` | PR #878 |
| One arm has unmatched trajectories | Comparison judge error or arm timeout biases the remaining trials | Report inconclusive, do not read it as a regression | PR #887 |
| Fixture resolves locally, SDK-not-found in CI | Fixture pin without roll-forward | `global.json` with `rollForward: latestMajor` | PR #907 |

## Fixtures

| Symptom | Cause | Fix | Evidence |
|---------|-------|-----|----------|
| Judge penalizes the agent for "pre-existing build issues" | A fixture meant to be healthy does not compile | Build every healthy fixture before shipping the eval; a deliberately broken one must fail only for the reason its stimulus is about | PR #949 |
| Scenario fails at setup in CI but passes locally | Fixture is on disk but not in the git index (`.gitignore` swallowed it) | Verify with `git ls-files`; the gate now blocks this | PR #945, PR #953 |
| Judge says the response "made a critical error" about a value the fixture supplies | The fixture states the same fact in two places and they disagree, so each arm can legitimately read a different truth — e.g. a Cobertura report whose declared `line-rate` contradicts its `<line>` payload or summary totals | Make every representation of the value agree, then re-derive any rubric or prompt that quotes it | PR #964, PR #945 |
| Baseline scores suspiciously well | The fixture never reproduces the bug the stimulus is named for | Rebuild the fixture until it produces the real error | PR #974 |
| `n` rose but power did not | Duplicate or rename-leftover fixtures wired in as new stimuli | Delete byte-equivalent leftovers; only wire fixtures exercising new behavior | PR #971, PR #945 |

## Statistical power

The gate has two independent bars: **distinct stimuli ≥ 5** (else `underpowered`), and **p ≤ 0.05
on an exact one-sided sign test over discordant (non-tie) stimulus votes**. Repeated runs collapse
to one vote per stimulus and remain reliability evidence.

| discordant stimulus votes | records that pass | p |
|---:|---|---:|
| ≤ 4 | none | ≥ 0.0625 |
| 5–7 | zero losses only (5W/0L) | 0.031 |
| 8 | one loss survivable (7W/1L) | 0.035 |

At exactly 5 stimuli one tie is fatal — it leaves 4 discordant votes. At 6 stimuli one tie is
survivable; at 7, up to two are. A loss is not: 4W/3T/1L over eight stimulus votes fails.

Consequences seen in real runs:

- Five `dotnet-test` evals raised to exactly 5 distinct stimuli returned 16W/8T/1L overall — every skill
  winning, none regressing — and **all five failed**, four because ties made a pass unreachable
  before the run started. (PR #971, `eng/eval-quality/README.md`)
- At the 32% tie rate measured there, a genuinely-helping skill parked at 5 stimulus votes is
  certified about one run in ten; at 15 stimulus votes, about nine in ten.
- Adding stimuli increases task breadth. Raising `runs` measures reliability for the same tasks and
  cannot clear the stimulus floor.
- The verdict reads each repeated run's **winner**, never its magnitude: weighting a confidence interval by
  "slightly better" vs "much better" made a stronger win look like variance and reversed verdicts on
  identical records. (PR #965, PR #952)

## Eval design

| Symptom | Cause | Fix | Evidence |
|---------|-------|-----|----------|
| A dormancy guard scores randomly across runs | Legacy results counted an identical-arm dormancy comparison in preference; `constraints.reject_skills` also prevents observing the activation contract | On schema version 4, use `expect_activation: false` alone; inspect `excludedScenarioEvidence` and `activationContract` instead of preference | PR #945, PR #953 |
| A reference skill shows no improvement | `disable-model-invocation: true` keeps it out of the model-facing skilled arm, so any direct eval compares identical arms | Remove the direct eval and cover the reference through consumer outcomes | PR #971, PR #976, issue #899 |
| An eval "passes" while the skill stopped emitting its signature output | No grader asserts the mandated shape | Add a grader for the exact contract (e.g. the `Recommendation:` line) | PR #904 |
| Overfit score high, user value unclear | Rubric items reward using the skill, or prompts echo skill vocabulary | Drop them: the harness already reports activation separately, so a rubric never needs to. Keep rubric items outcome-shaped and de-cue the prompt | PR #904 |
| Both arms produce the same kind of artifact and the judge falls back on comparing volume | The rubric rewards raw output instead of the property under test | Add anti-hijack criteria: do not invoke the skill, and do not reward quantity (number of tests, findings, or lines produced) | PR #945 |
| A grader appears to enforce something but does not | `config:` is missing its required key after an indentation slip | `check_eval_quality.py` blocks it; verify the key is present | `eng/eval-quality/README.md` |
| Two stimuli behave identically | Duplicate YAML key — a leftover `prompt:`/`graders:` block overwrites the following stimulus field by field | Delete the stray block after confirming it is not a distinct stimulus that lost its `- name:` | PR #971 |
| Eval measures path recall | The skill is a map to reference files | Do not create the eval; test the consumer's outcome instead | PR #974 |

## Activation

| Symptom | Cause | Fix | Evidence |
|---------|-------|-----|----------|
| Not activated in any arm | Triggering words absent from `description` | Add symptoms, error codes, artifact names in user language | PR #974 |
| Not activated for the scenarios the skill exists for | An over-broad exclusion clause | Re-read every "do not use for" clause against every eval prompt | PR #974 |
| Wrong sibling wins the prompt | Descriptions partitioned by topic instead of by discriminator | Partition on the real question, and add handoff exclusions on both sides | PR #864 |
| Sibling wins on one ambiguous word | The target skill never claims that word | Claim it explicitly — "review" had to be claimed by `writing-mstest-tests` | PR #863 |
| Isolated activation perfect, plugin arm fails | The model self-serves: reads the file and answers with no skill at all | Raise stakes in the description, de-crowd the menu, verify in the plugin arm | PR #850 |
| Menu pressure across a plugin | Helper/reference skills consuming budget | `disable-model-invocation: true` keeps them invocable by name only | PR #850 |

## Process

| Rule | Evidence |
|------|----------|
| Trigger `/evaluate` by submitting a PR review (Files changed → Review changes) so the run binds to the reviewed commit | PR #956, PR #949 |
| Before declaring a regression, confirm the invoked payload changed — reruns on byte-identical content moved 7W/2T/2L to 4W/5T/2L | PR #974 |
| Use cross-family evaluation for broad rewrites; single-family passes hide model-specific regressions | issue #899, PR #947 |
| Workflow changes cannot be validated by the PR's own evaluation (GitHub runs workflow definitions from `main`) — use manual dispatches | PR #872 |
| Prefer deterministic scripts over agentic workflows for deterministic policy | PR #928 |
| Agent `tools:` allowlists are host-specific and case-sensitive; an allowlist can grant zero tools | PR #856, PR #847 |
| Keep `InvestigatingResults.md` in sync whenever verdict fields, scoring, or PR-comment wording change | PR #965, PR #932 |
