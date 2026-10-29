# Judge Model Study: Saved-Artifact Comparison

## Decision

Keep the current judge on both executor routes. No candidate showed stronger alignment with deterministic evaluation checks than the current judge. If a candidate must be chosen for a later controlled trial, use MAI-Code-2-Flash rather than Haiku 5.5 for GPT executor results, and GPT-6.1-Terra rather than MAI-Code-2-Flash for Claude executor results. These are candidate-only rankings; neither candidate beat the current judge on the primary quality signal.

| Executor results | Rank | Judge | Check alignment on differing-check cases |
|---|---:|---|---:|
| GPT-5.6-Luna | 1 | Current: Claude Haiku 4.5 | 141/192 (73.4%) |
| GPT-5.6-Luna | 2 | MAI-Code-2-Flash | 103/192 (53.6%) |
| GPT-5.6-Luna | 3 | Claude Haiku 5.5 | 78/192 (40.6%) |
| Claude Sonnet 5 | 1 | Current: GPT-5.6-Terra | 132/171 (77.2%) |
| Claude Sonnet 5 | 2 | GPT-6.1-Terra | 117/171 (68.4%) |
| Claude Sonnet 5 | 3 | MAI-Code-2-Flash | 90/171 (52.6%) |

These are route-specific rankings, not one global model ranking. Cross-family grading means the judge candidates were tested on different executor families, with different current judges and check-disagreement subsets.

## Study design

The study compared each candidate with the current judge on the same saved evaluation results. It did not rerun executor evaluations. The first phase was the 38-trial MSBuild pilot. After checking that the replay used saved evidence and obeyed the cross-family rule, the study expanded to 35 selected artifacts per executor route across the available plugins.

| Executor family | Saved executor | Current judge | Candidate judge(s) |
|---|---|---|---|
| GPT | GPT-5.6-Luna | Claude Haiku 4.5 | Claude Haiku 5.5; MAI-Code-2-Flash |
| Claude | Claude Sonnet 5 | GPT-5.6-Terra | GPT-6.1-Terra; MAI-Code-2-Flash |

The source was evaluation run [39278015789](https://github.com/dotnet/skills/actions/runs/39278015789), at commit `c597899445185eeb06ef782f3d5fd907cdd8daa4`. Each candidate replay used the saved artifact IDs selected by the CI workflow. For each executor route, all candidates used the same 28 safely replayed artifact IDs. The other seven of 35 artifacts per route could not be paired: legacy result files had multiple trials but no `trialIndex`. The replay does not infer a trial pairing from array order, because that could compare different trials.

The broad CI runs were:

| Candidate | CI run |
|---|---|
| MAI-Code-2-Flash on GPT results | [40789338426](https://github.com/dotnet/skills/actions/runs/40789338426) |
| MAI-Code-2-Flash on Claude results | [40789338033](https://github.com/dotnet/skills/actions/runs/40789338033) |
| GPT-6.1-Terra on Claude results | [40789338206](https://github.com/dotnet/skills/actions/runs/40789338206) |
| Claude Haiku 5.5 on GPT results | [40789338539](https://github.com/dotnet/skills/actions/runs/40789338539) |

The CI aggregate jobs failed because they expected reports for all 35 selected artifacts and received only the 28 safely paired reports. This was a coverage failure, not a candidate judgment error. The successful replay artifacts were then compared using the same expected source artifact IDs. No candidate judgment errors occurred in the usable artifacts.

## Method and measures

The replay workflow rejudged saved Vally or native evaluation evidence, checked that the executor model stayed unchanged, and compared candidate verdicts with the saved current-judge verdicts. The replay code rejects same-family judge/executor pairings and requires exact trial-key matches.

The study uses these measures:

- **Check alignment:** Of trials where the deterministic baseline and treatment checks differ, the share of judges whose winner matches the passing side. This is the primary available quality signal, but it is not ground truth.
- **Agreement, direct reversal, and tie shift:** How each candidate verdict compares with the current judge. Agreement is not proof of correctness. A tie shift is a change between a tie and a non-tie; a direct reversal is a change between baseline and treatment.
- **Candidate token use and judge wall time:** Sums from the CI usage tap for complete candidate pairwise calls. These values do not include current-judge usage.

The report ranks quality first. Token use and judge wall time break ties or help compare candidates whose quality signals are similar. No combined score was calculated.

## Broad results

Each row below compares a candidate with that route's current judge over the same successfully replayed artifacts.

| Executor route | Candidate | Matched trials | Agreement | Direct reversals | Tie shifts | Candidate errors | Candidate check alignment |
|---|---|---:|---:|---:|---:|---:|---:|
| GPT-5.6-Luna | Claude Haiku 5.5 | 560 | 265 | 13 | 282 | 0 | 78/192 (40.6%) |
| GPT-5.6-Luna | MAI-Code-2-Flash | 560 | 316 | 19 | 225 | 0 | 103/192 (53.6%) |
| Claude Sonnet 5 | GPT-6.1-Terra | 559 | 380 | 35 | 144 | 0 | 117/171 (68.4%) |
| Claude Sonnet 5 | MAI-Code-2-Flash | 559 | 331 | 24 | 204 | 0 | 90/171 (52.6%) |

For comparison, the current judges aligned with the passing side on 141/192 GPT-route cases (73.4%) and 132/171 Claude-route cases (77.2%). The count of cases with differing deterministic checks is the denominator for alignment; it is smaller than the matched-trial count.

## Token use and duration

Usage was complete for 481/560 matched trials on the GPT route and 480/559 on the Claude route. This represents 19 of 28 artifacts per candidate. The table reports totals for complete candidate calls only.

| Route | Candidate | Complete calls | Input tokens | Output tokens | Total tokens | Candidate judge wall time |
|---|---|---:|---:|---:|---:|---:|
| GPT-5.6-Luna | Claude Haiku 5.5 | 481 | 48,904,251 | 2,511,377 | 51,415,628 | 7,225,979 ms |
| GPT-5.6-Luna | MAI-Code-2-Flash | 481 | 50,166,126 | 1,841,768 | 52,007,894 | 17,590,944 ms |
| Claude Sonnet 5 | GPT-6.1-Terra | 480 | 52,867,985 | 1,134,853 | 54,002,838 | 9,813,990 ms |
| Claude Sonnet 5 | MAI-Code-2-Flash | 480 | 44,453,753 | 1,768,824 | 46,222,577 | 16,642,317 ms |

On GPT results, MAI used about 1.2% more total tokens and about 2.4 times the candidate judge wall time of Haiku 5.5. MAI's better check alignment than Haiku 5.5 does not make it a replacement for the current judge.

On Claude results, GPT-6.1-Terra used about 16.8% more total tokens than MAI, but its check alignment was higher and its candidate judge wall time was about 41% lower. It is the stronger of these two candidates for this route, but it still fell below the current judge's check alignment.

The current-judge token totals are not recorded in the source artifacts. The broad results do not support a token-cost comparison between any candidate and its current judge. The wall-time sums are candidate call time, not end-to-end run time; CI jobs ran concurrently.

## Pilot and prior Haiku evidence

The MSBuild pilot used 38 trials per pairing and confirmed that the CI replay could use saved results without running executor evaluations. It found no judgment errors.

| Candidate and route | Usage coverage | Candidate tokens (input + output) | Candidate judge wall time | Check alignment |
|---|---:|---:|---:|---:|
| Claude Haiku 5.5 on GPT results | 33/38 trials | 2,196,341 + 140,833 | 436,231 ms | 4/10; current Haiku 4.5 was 8/10 |
| MAI-Code-2-Flash on GPT results | 33/38 trials | 2,862,596 + 100,030 | 934,837 ms | 5/10; current Haiku 4.5 was 8/10 |
| GPT-6.1-Terra on Claude results | 33/38 trials | 2,792,915 + 68,054 | 593,150 ms | 5/8; current GPT-5.6-Terra was 5/8 |
| MAI-Code-2-Flash on Claude results | 33/38 trials | 2,711,005 + 102,709 | 918,452 ms | 3/8; current GPT-5.6-Terra was 5/8 |

The earlier Haiku 5.5 study is reused here; its case analysis was not repeated. That study covered 333 paired trials across 15 artifacts, with 232 agreements, 14 direct reversals, and 87 tie shifts. On the 106 trials with differing deterministic checks, Haiku 4.5 aligned on 82 and Haiku 5.5 on 84. This small difference did not show a clear quality gain. A 10-trial timing pilot recorded 217.06 seconds for Haiku 4.5 and 76.88 seconds for Haiku 5.5. Three reversals had user decisions; 11 remained unresolved.

The broad results add evidence against changing the GPT route to Haiku 5.5: it aligned less often with deterministic checks than both the current judge and MAI-Code-2-Flash. The prior timing result is limited to its 10-trial pilot and is not directly comparable with the broad candidate call-time totals.

## Focused case checks

Three broad cases received focused manual review. These cases are useful examples, not a representative sample:

- MAI-Code-2-Flash correctly chose `noop` for “Filter intentionally broken fixture changes,” consistent with the rubric in `tests/agentic-workflows/msbuild-quality-review/eval.yaml`.
- The Haiku 5.5 choice on a pytest anti-pattern case was mixed; deterministic checks favored the old baseline. See `tests/dotnet-test/test-anti-patterns/eval.yaml`.
- GPT-6.1-Terra's choice on an MSTest migration-plan case did not establish a clear quality gain. See `tests/dotnet-test-migration/agent.test-migration/eval.yaml`.

These checks do not outweigh the broad alignment results or establish correctness on cases without a reliable reference answer.

## Limits

1. Deterministic check alignment is a proxy. The checks can disagree with the rubric, and agreement with the current judge is not proof that either judge is correct.
2. A tie is counted as not aligned when the deterministic checks differ. The reported alignment therefore reflects the study's explicit scoring rule.
3. Seven of 35 artifacts per route were excluded because they lacked safe trial pairing. The conclusion applies to the 28 matched artifacts, not the excluded artifacts.
4. Candidate token and duration data cover only 19 of 28 artifacts. Current-judge token use is unavailable, so candidate-versus-current resource savings cannot be claimed.
5. Candidate judge wall time is the sum of logged call times. It is not end-to-end elapsed time, and parallel CI jobs affect total study duration.
6. Only three broad cases received focused manual review. More human review with adjudicated reference answers would be needed to establish correctness beyond the available checks.

## Recommendation

Keep Claude Haiku 4.5 for GPT-5.6-Luna results and GPT-5.6-Terra for Claude Sonnet 5 results. The current judges lead on the primary quality signal for their routes. If another candidate-only comparison is useful, test MAI-Code-2-Flash against Haiku 5.5 for GPT results only if its higher call time is acceptable; for Claude results, prefer GPT-6.1-Terra over MAI because it has better check alignment and lower call time, while accounting for its higher token total. Do not treat either candidate recommendation as evidence to replace the current judge.
