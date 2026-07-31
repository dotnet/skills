# Writing skill content that beats the baseline

Every skill in this repo is scored head-to-head against the *same model with no skill loaded*.
The score is a **delta**, so content the model already produces unaided is worth zero — and
content that makes the model slower, longer, or more hedged is worth less than zero.

Each rule below is traced to the PR where it was learned.

## 1. Encode decisions, not knowledge

**Rule:** Write what the model should *do* when it sees a symptom, not what an API *is*.

`system-text-json-net11` scored 0% improvement because it "read as reference prose — API
signatures the model already reproduces". The rewrite into imperative decision guidance is what
moved it. (PR #926)

Practical test: delete any sentence the unskilled model would have written anyway. If most of the
skill disappears, it is a reference doc, not a skill.

## 2. Use "when A, do B, never C, verify D" tables

**Rule:** Route with decision tables so the model picks one answer instead of listing plausible
alternatives.

PR #926 added a table mapping PascalCase / typed-metadata / probing requests to exact APIs and
their anti-patterns. PR #947 added "Rules That Change the Answer" tables across the MAUI skills.
Tables also give graders something concrete to assert on.

## 3. Demand a concrete final artifact

**Rule:** Specify the exact shape of the answer — the command, the verdict line, the findings
table, the recommendation.

Generic advice did not move the template-engine skills; output contracts did: "exact single
`dotnet new` command", "one-line verdict header", "single findings table", decisive
`Recommendation:` lines. (PR #904)

If the skill mandates an output shape, the eval must assert on that shape, or the skill can
silently stop emitting it.

## 4. Scale structure to input size

**Rule:** Preserve the full report template for large inputs; answer directly for small ones.

`assertion-quality` regressed on an 8-test fixture because the skill forced a "Summary Dashboard +
12 categories" onto a trivial input and lost to a concise baseline. The fix instructed the skill to
scale report depth to suite size and complexity. (PR #865)

## 5. Add stop-conditions — then check you have not over-corrected

**Rule:** Say when *not* to act; then verify the skilled answer is still at least as complete as
the baseline's.

Strong skills regress by doing too much: rewriting working code, answering beyond the question,
prescribing a remedy before measuring. PR #910 added stop-conditions to `eval-performance` so it
would not act on compile-time slowness or unmeasured builds; PR #947 added scope control to
`maui-collectionview`.

The opposite failure is just as real: in PR #947 the scope-control wording "over-corrected the
other skills into answering too narrowly", and failing scenarios had skilled answers *shorter* than
baseline. Length is not the goal, but omitting the implementation detail the baseline supplied is a
loss.

## 6. Do not make discoverable inputs "required"

**Rule:** Only mark an input required when a human genuinely must supply it. Otherwise instruct the
agent to discover it.

A `Project or solution path | Yes` row in an Inputs table made the agent answer "I need to see your
project file" while `TestProject.csproj` sat in the working directory. (PR #974)

## 7. Verify load-bearing claims empirically

**Rule:** Compile or probe anything the skill asserts about API surface or runtime behavior.

The most damaging skill defects found in review were factual: MAUI docs taught `ItemSizingStrategy`
on `LinearItemsLayout`, which does not compile (MAUIX2002), and a theming "fix" was reverted after a
runtime probe disproved the source-reading that motivated it. (PR #947)

## 8. Preserve semantics in migration mappings

**Rule:** Migration tables need semantic guardrails, not just API substitutions.

A one-cell mapping steered frontier models into a behavior change: `TimeProvider` guidance using
`.DateTime` silently set `DateTimeKind.Unspecified`; the correct mapping is `.UtcDateTime` /
`.LocalDateTime`. (PR #906)

## 9. Require truthful validation reporting

**Rule:** Tell the agent to distinguish restore, build and test failures, and to cite a clean run
before claiming success.

`migrate-static-to-wrapper` lost trials for claiming "Build succeeded" after a restore failure;
`code-testing-agent` had to be told to cite a clean run. (PR #945)

## 10. Prove already-correct inputs are left alone

**Rule:** Any skill that migrates or rewrites code must state the no-op condition, and the eval must
test it.

PR #929 added an "already on v3" boundary fixture verifying no changes were made; the reviewer
called it "the single best addition" in the PR.

## 11. Keep the common path in `SKILL.md`, gate the rest

**Rule:** Rare, expensive or platform-specific paths belong behind a `references/` read.

`coverage-analysis` went from 30 KB to 15 KB by moving PowerShell and report-generation paths into
references read only when needed — cost down, contract unchanged. (PR #971)

## 12. Size orchestration to the request

**Rule:** Do not run a full research → plan → implement pipeline for one function.

`code-testing-agent` was split into focused and broad paths so a single-function request skips
`.testagent/` artifacts and extra passes. (PR #971)

## 13. Structure beats verbosity

**Rule:** When a scenario ties despite a longer skilled answer, the missing differentiator is a
recommended shape, not more words.

Both arms hardcoded colors in the MAUI theming scenario. The winning change was the rule "define
the palette once — don't scatter literals", not a longer explanation. (PR #947)

## 14. Retirement is a legitimate outcome

**Rule:** A skill that is weak across model families, thinly used, and costing menu budget should be
cut, not polished indefinitely.

`mcp-csharp-debug` was cut after strengthening 0 of 5 families, with owner confirmation; the change
removed the skill, its eval, its CODEOWNERS entry and cross-skill references. (PR #938)
`dotnet-test-frameworks` was removed as a duplicate subset that "nothing actually loaded". (PR #851)

## Frontmatter: the description is the router

The `description` is the only text the runtime sees when deciding whether to load the skill. (PR #974)

| Rule | Evidence |
|------|----------|
| Include symptoms, error codes and artifact names in the user's words (`CS1501`, `.testsettings`, `MSTEST0014`) | PR #974 |
| Lead with an action verb and quote natural requests ("what's wrong with my build file?") | PR #910 |
| Partition siblings by the real discriminator ("abstraction already exists" vs "create wrapper first"), with exclusions on both sides | PR #864 |
| Claim the ambiguous words that route to the wrong sibling — `writing-mstest-tests` had to claim "review" | PR #863 |
| Never exclude a phase the skill exists to serve; "already on MSTest v3+" blocked its own post-bump fixtures | PR #974 |
| Watch both limits: 1,024 characters per description **and** the plugin menu budget | PR #974, PR #910 |
| A helper skill that users should not invoke can set `disable-model-invocation: true` to free menu budget | PR #850 |

Menu pressure is measurable: disabling model invocation for one helper skill dropped the
`dotnet-test` menu from 14,981 to 14,261 characters (PR #850), and PR #910 tracked the msbuild menu
budget explicitly as part of a description rewrite.
