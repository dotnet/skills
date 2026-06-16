# Quality modes — LLM-judge

How quality mode runs the agent against `golden.json` and asks a judge
model to score each response per the rubric.

## Runner

```csharp
public sealed class QualityEvalRunner : IEvalRunner
{
    public async Task<EvalReport> RunAsync()
    {
        var rubric = await File.ReadAllTextAsync("Quality/rubric.md");
        var golden = await JsonSerializer.DeserializeAsync<GoldenInput[]>(
            File.OpenRead("Quality/golden.json"));

        var judge = new ChatClientBuilder()
            .UseFunctionInvocation()
            .Build(new ChatClient(model: Config.JudgeModel, apiKey: Config.JudgeApiKey));

        var rows = new List<QualityRow>();
        foreach (var g in golden!)
        {
            var actual = await Agent.RunAsync(g.UserMessage);
            var verdict = await Judge(judge, rubric, g, actual);
            rows.Add(new QualityRow(g.Id, verdict.Scores, verdict.PassFail, verdict.Rationale));
        }

        return EvalReport.FromQuality(rows);
    }
}
```

## Judge prompt skeleton

```
You are a quality judge. Score the assistant response on each trait
in the rubric (1-5). Return ONLY a JSON object of the form:

{ "scores": { "<trait>": <int>, ... }, "rationale": "<one sentence>" }

Rubric:
{{ rubric_md }}

User asked:
{{ user_message }}

Assistant replied:
{{ actual_response }}

Required traits to score:
{{ expected_traits }}
```

## Pass/fail

`quality.thresholds.json` (optional, user-edited):

```json
{
  "mean_score_min": 4.0,
  "per_trait_min": 3,
  "fail_on_threshold_breach": false
}
```

If `fail_on_threshold_breach: false` (default), the runner exits 0
even on quality regressions and marks them in the report. Set to
`true` to gate CI.

## Report — `quality.md`

```markdown
# Quality — {{ utc_timestamp }}

Judge: {{ judge_model }}  |  Inputs: {{ count }}  |  Pass rate: {{ pct }}

| Id | Mean | on_topic | concise | safe | format | Pass | Rationale                       |
|----|------|----------|---------|------|--------|------|---------------------------------|
| g1 | 4.5  |    5     |    4    |  5   |   4    |  ✅  | Concise with clear sections.    |
| g2 | 3.0  |    4     |    2    |  3   |   3    |  ❌  | Rambling intro; over 6 sentences. |

## Failures

### g2 (3.0)
- judge rationale: ...
- actual response (truncated): ...
```

## Cost considerations

Each quality run pays for: agent calls (real or stub) + judge calls
(always real). Expect roughly 1 judge call per input. Use a smaller
judge model (e.g. `gpt-4o-mini`) for early iterations and switch up
when stabilizing.
