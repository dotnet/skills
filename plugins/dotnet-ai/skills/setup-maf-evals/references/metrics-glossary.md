# Metrics glossary

Source of truth for the per-run `metrics-glossary.md` artifact that the
scaffolded `<App>.Evals.Tests/Reporting/MetricsGlossary.cs` writes next
to `report.html`. The aieval HTML report is data-bound JSON — it shows
numbers but no definitions — so we co-locate this glossary with the
report so a first-time reader has a one-page cheat-sheet.

The skill emits **only the entries for evaluators that actually ran** in
the active tier, so a stub-tier user sees Words/BLEU/GLEU/F1 and
nothing else.

## NLP tier (deterministic, no LLM)

### `Words`
- **Custom evaluator** scaffolded by this skill (see `evaluators-catalog.md`).
- **What it measures:** raw token count of the response text.
- **Scale:** integer ≥ 0.
- **Interpretation:** `< 5` → Poor (response too short / empty). `5–500` → Good. `> 500` → Average (response unusually long).
- **Trust for:** sanity-checking that the model produced *anything* and isn't running away with a 10-page essay.
- **Don't trust for:** quality. A 50-word wrong answer scores the same as a 50-word correct answer.

### `BLEU` — Bilingual Evaluation Understudy
- **What it measures:** n-gram (1-4) overlap between the response and one or more reference strings, with a brevity penalty.
- **Scale:** 0.0 – 1.0.
- **Interpretation:** ~0.0–0.1 weak overlap, often paraphrased; ~0.1–0.3 normal for free-form generation; ~0.3–0.5 strong; > 0.5 near-quotation.
- **Trust for:** "is the response in the same lexical neighbourhood as the reference?" — useful as a regression signal when the reference is canonical.
- **Don't trust for:** semantic correctness. A correct paraphrase scores low; an incorrect copy-paste of reference fragments scores high.
- **Reference:** [`BLEUEvaluator` in MEAI.Evaluation.NLP](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.evaluation.nlp.bleuevaluator).

### `GLEU` — Google-BLEU
- **What it measures:** sentence-level BLEU variant. Symmetric — penalises both missing reference n-grams and extra invented ones.
- **Scale:** 0.0 – 1.0.
- **Interpretation:** same buckets as BLEU; GLEU usually tracks BLEU but is less brittle on short outputs.
- **Trust for:** the same use cases as BLEU when individual scenarios are short (1-2 sentences) and BLEU's brevity penalty would be misleading.
- **Don't trust for:** any semantic claim. Same caveats as BLEU.
- **Reference:** [`GLEUEvaluator`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.evaluation.nlp.gleuevaluator).

### `F1` — Token-level F1
- **What it measures:** harmonic mean of unigram precision and recall against the ground-truth string. Order-insensitive.
- **Scale:** 0.0 – 1.0.
- **Interpretation:** ~0.0–0.2 mostly-disjoint vocab; 0.3–0.5 typical for free-form generation; > 0.6 strong word-level match.
- **Trust for:** QA / extraction-style scenarios where the *set of words* matters more than phrasing (SQuAD-style benchmarks use this).
- **Don't trust for:** word-order-sensitive tasks (e.g., "yes" vs "no" answers buried in long output) — the F1 score will be high even if the polarity is wrong.
- **Reference:** [`F1Evaluator`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.evaluation.nlp.f1evaluator).

> **NLP-tier headline:** all three of BLEU/GLEU/F1 are *lexical* metrics.
> They're cheap, deterministic, and free, but they cannot tell you the
> response is *correct* — only that it's *similar in wording*. Treat them
> as a regression early-warning, not a quality verdict.

## Quality tier (LLM-as-judge — `EVAL_USE_REAL_JUDGE=1`)

All Quality metrics produce a 1-5 `EvaluationRating` (Poor → Excellent)
plus a free-text rationale from the judge model.

### `Relevance`
- **What it measures:** does the response actually address the user's query?
- **Trust for:** catching off-topic regressions (model started monologuing about a different topic).
- **Don't trust for:** factual correctness — a *relevantly wrong* answer can score high.

### `Coherence`
- **What it measures:** is the response logically structured / orderly / easy to follow?
- **Trust for:** detecting rambling, contradictory, or logically broken outputs.
- **Don't trust for:** depth or correctness — coherent nonsense scores high.

### `Fluency`
- **What it measures:** grammar, readability, naturalness.
- **Trust for:** detecting broken-English / token-soup outputs from undertrained or quantised models.
- **Don't trust for:** anything beyond surface text quality. A fluent lie scores high.

### `Completeness`
- **What it measures:** how comprehensive and accurate the response is, given a reference (`CompletenessEvaluatorContext(groundTruth)`).
- **Trust for:** catching responses that are correct but partial (covered 2 of 4 required points).
- **Don't trust for:** brevity-as-a-feature scenarios — a short-but-correct answer can score lower than a long-and-padded one.

### `Equivalence`
- **What it measures:** semantic similarity between response and ground truth in the context of the original query.
- **Trust for:** distinguishing "right answer, different words" (high) from "wrong answer" (low). Better than BLEU/F1 when paraphrasing is acceptable.
- **Don't trust for:** any case where the ground truth is itself ambiguous or one of multiple valid answers.

### `Groundedness`
- **What it measures:** alignment between the response and a supplied source-of-truth context (`GroundednessEvaluatorContext(context)`).
- **Trust for:** RAG pipelines — flags when the model invented facts not in the retrieved context.
- **Don't trust for:** open-ended chat without a context document.

### Agentic-only

The skill wires these only when an `*.AppHost.csproj` is detected.

- **`Intent Resolution`** — did the model identify and resolve the user's actual intent (vs answering a related-but-different question)?
- **`Task Adherence`** — did the model stick to the assigned task or wander into other agent territory?
- **`Tool Call Accuracy`** — did the model invoke the right tools with the right arguments? Requires `ToolCallAccuracyEvaluatorContext`.

> **Quality-tier headline:** these are LLM-judge subjective scores. They
> drift across judge model versions. Pin the judge model in
> `quality.thresholds.json` if you want comparable scores across runs.

## Safety tier (Foundry — `EVAL_USE_FOUNDRY_SAFETY=1`)

All Safety evaluators return a 1-5 severity (1 = safe, 5 = severe harm)
plus a confidence score. Each metric is an *output classifier* — it
inspects the model's response, not the input prompt.

### `Hate And Unfairness`, `Self Harm`, `Violence`, `Sexual`
- Wired together as the single-shot `ContentHarmEvaluator` (one Foundry call → all 4 metrics).
- **Trust for:** detecting harmful content in agent outputs.
- **Don't trust for:** input-side filtering — these never see the user's prompt. Pair with input-side Azure AI Content Safety for full coverage.

### `Protected Material`
- Detects copyrighted text / song lyrics / book passages reproduced in the output.

### `Indirect Attack`
- Detects prompt-injection-style content that would indicate the model picked up an indirect attack from retrieved content or tool output. Closest thing to an input-side check available in this tier.

### `Code Vulnerability`
- Flags vulnerable patterns in code the model emitted (SQL injection, hardcoded credentials, weak crypto, etc.).

### `Ungrounded Attributes`
- Detects inferred human attributes (race, gender, age, religion, etc.) in the response that weren't in the input.

### `Groundedness Pro`
- Foundry-hosted fine-tuned groundedness evaluator. More accurate than the open-source `GroundednessEvaluator` but costs a Foundry call per scenario.

> **Safety-tier headline:** all safety metrics are *output classifiers*.
> They protect downstream consumers from the agent's outputs; they do not
> protect the agent from its inputs.

## Quick reference card

| Metric | Tier | Scale | Needs ground truth? | Needs LLM? |
|--------|------|-------|---------------------|-----------|
| Words | NLP | int | no | no |
| BLEU | NLP | 0-1 | yes (references) | no |
| GLEU | NLP | 0-1 | yes (references) | no |
| F1 | NLP | 0-1 | yes (one string) | no |
| Relevance / Coherence / Fluency | Quality | 1-5 | no | yes (judge) |
| Completeness / Equivalence | Quality | 1-5 | yes (one string) | yes (judge) |
| Groundedness | Quality | 1-5 | yes (context) | yes (judge) |
| Intent Resolution / Task Adherence | Quality (agentic) | 1-5 | no | yes (judge) |
| Tool Call Accuracy | Quality (agentic) | 1-5 | yes (expected_tool_calls) | yes (judge) |
| Content Harm bundle (4 metrics) | Safety | 1-5 severity | no | yes (Foundry) |
| Protected / Indirect / Code Vuln. / Ungrounded | Safety | 1-5 severity | varies | yes (Foundry) |
| Groundedness Pro | Safety | 1-5 | yes (context) | yes (Foundry) |

## `MetricsGlossary.cs` template

The scaffolded `<App>.Evals.Tests/Reporting/MetricsGlossary.cs` writes
the tier-relevant slice of this glossary next to `report.html` after
each `dotnet test` run.

> **MSTest constraint:** an assembly may declare **only one**
> `[AssemblyCleanup]` method. The skill emits `MetricsGlossary` as a
> plain static class (no `[TestClass]`, no `[AssemblyCleanup]`) and
> chains `MetricsGlossary.WriteGlossary()` from
> `AievalReport.GenerateReport`'s single `[AssemblyCleanup]`.
> Wrap the call in a `try/catch` so a glossary-write failure never
> masks the report.

```csharp
internal static class MetricsGlossary
{
    public static void WriteGlossary()
    {
        var outDir = Path.Combine(
            RepoRoot.Find(), ".copilot", "perf-reports", "evals", EvalEnv.ReportFolder);
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "metrics-glossary.md");

        var sb = new StringBuilder();
        sb.AppendLine($"# Metrics glossary — {EvalEnv.Tier} tier");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine($"Companion to `report.html` in this folder. The aieval HTML report shows numbers; this file explains them.");
        sb.AppendLine();

        sb.AppendLine(NlpEntries);
        if (EvalEnv.UseRealJudge) sb.AppendLine(QualityEntries);
        if (EvalEnv.UseFoundrySafety) sb.AppendLine(SafetyEntries);

        sb.AppendLine();
        sb.AppendLine("> Source: setup-maf-evals references/metrics-glossary.md");

        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"[MetricsGlossary] {path}");
    }

    private const string NlpEntries = """
        ## NLP tier (deterministic, no LLM)
        - **Words** (int): response length sanity check. <5 too short, 5-500 ok, >500 long.
        - **BLEU** (0-1): n-gram overlap with reference(s). 0.1-0.3 normal, >0.3 strong, >0.5 near-quotation. *Lexical, not semantic.*
        - **GLEU** (0-1): sentence-level BLEU; better for short outputs. Same buckets as BLEU.
        - **F1** (0-1): unigram token F1 vs ground-truth. 0.3-0.5 typical, >0.6 strong word-level match. Order-insensitive.

        > Headline: NLP metrics measure wording similarity, not correctness. Use for regression early-warning, not as quality verdicts.
        """;

    private const string QualityEntries = """
        ## Quality tier (LLM-as-judge)
        Each rated 1-5 (Poor → Excellent) with a free-text rationale.
        - **Relevance**: addresses the user's query. Catches off-topic regressions.
        - **Coherence**: logically structured. Catches rambling/contradictory outputs.
        - **Fluency**: grammar/readability. Catches broken-English outputs.
        - **Completeness** (needs reference): comprehensive and accurate.
        - **Equivalence** (needs reference): semantic similarity in context of the query.
        - **Groundedness** (needs context): aligned with supplied source-of-truth.
        - **Intent Resolution / Task Adherence / Tool Call Accuracy** (agentic only).

        > Headline: judge scores drift across model versions. Pin the judge model for comparable runs.
        """;

    private const string SafetyEntries = """
        ## Safety tier (Foundry)
        Each rated 1-5 severity (1 safe → 5 severe).
        - **ContentHarm bundle** (single-shot, 4 metrics): Hate-And-Unfairness, Self-Harm, Violence, Sexual.
        - **Protected Material**: copyrighted text reproduced.
        - **Indirect Attack**: prompt-injection content from retrieved/tool data.
        - **Code Vulnerability**: vulnerable code patterns (SQLi, weak crypto, etc.).
        - **Ungrounded Attributes**: inferred human attributes not in input.
        - **Groundedness Pro**: Foundry-hosted fine-tuned groundedness check.

        > Headline: all safety metrics inspect *outputs*, not inputs. Pair with Azure AI Content Safety on the request side for full coverage.
        """;
}
```

Then in `Reporting/AievalReport.cs` (the assembly's single
`[AssemblyCleanup]` host):

```csharp
[TestClass]
public static class AievalReport
{
    [AssemblyCleanup]
    public static void GenerateReport()
    {
        // ... aieval invocation ...

        try { MetricsGlossary.WriteGlossary(); }
        catch (Exception ex) { Console.Error.WriteLine($"[MetricsGlossary] Failed: {ex.Message}"); }
    }
}
```

The skill should emit both files verbatim — change the `private const`
strings only if upstream MEAI changes a metric definition.
