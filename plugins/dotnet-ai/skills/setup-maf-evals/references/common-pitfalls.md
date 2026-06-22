# Common pitfalls

Footguns that turned up while building and dogfooding this skill.
Avoid them when scaffolding `<App>.Evals.Tests`.

## Reporting pipeline

- **Multiple `[AssemblyCleanup]` methods.** MSTest forbids more than
  one `[AssemblyCleanup]` per assembly (UTA014 at discovery time —
  every test in the assembly fails to load). The
  `MetricsGlossary.WriteGlossary` write must be **chained from
  `AievalReport.GenerateReport`'s single `[AssemblyCleanup]`**, not
  declared as its own. Wrap the chained call in a `try / catch` so a
  glossary-write failure never masks the report.
- **Hand-rolling reports instead of using the Reporting pipeline.**
  The whole point of GA `Microsoft.Extensions.AI.Evaluation.Reporting`
  10.7.0 is `DiskBasedReportingConfiguration` + `aieval`. Never write
  a hand-rolled markdown report and call it the "quality report" —
  that's an MEAI report (HTML) vs a cost/latency capture (markdown).
- **Treating telemetry / compare / quality as separate report
  streams.** Compare mode goes through `ReportingConfiguration` with
  a distinct `executionName` per matrix entry, so `aieval report`
  aggregates them into the same HTML.
- **Forgetting the per-run `metrics-glossary.md`.** The aieval HTML is
  data-bound JSON; it shows numbers but no definitions. Co-locate
  `metrics-glossary.md` (tier-aware) so a first-time reader can decode
  the columns. The `Reporting/MetricsGlossary.cs` template handles
  this — don't strip it.
- **Misreading "Cache Miss" in the Diagnostic Data section.** With
  `enableResponseCaching: true`, the report's per-call `cacheHit` flag
  records whether the judge response cache was hit. Expect **Miss on
  every call** in the `real agent + real judge` workflow — the cache
  key is the judge's input prompt, which includes the agent's response,
  and a live LLM agent's output varies run-to-run even at low
  temperature. Hit/Miss is informational; it does not affect
  correctness or scores. The cache pays off when you (a) capture agent
  responses to fixtures and re-evaluate them while iterating on
  rubrics, or (b) run a stub agent with deterministic output. To get
  hits across runs you ALSO need to pin `executionName` (the cache is
  scoped per execution name; a fresh timestamp per run guarantees
  misses regardless of input).

## Clients (agent vs judge vs stub)

- **Calling real models from the default test run.** Stub tier uses
  `StubChatClient`; the report banner is clearly marked
  `(stub IChatClient)`. Real-model runs are opt-in via three
  independent env vars.
- **Conflating agent and judge clients.** Two different `IChatClient`
  roles. The skill exposes them as two independent env vars
  (`EVAL_USE_REAL_AGENT`, `EVAL_USE_REAL_JUDGE`) — one can be real
  while the other is stubbed.
- **Auto-detected factory throwing a generic NRE on missing config.**
  When the app uses Aspire orchestration, `dotnet test` runs outside
  the AppHost and `ConnectionStrings:<alias>` is unset. The factory
  template in `ichatclient-detection.md` wraps DI resolution in a
  `try / catch` that throws a friendly `InvalidOperationException`
  naming the connection-string key and the user-secrets command. Don't
  strip this when adapting the template.
- **User-secrets silently not loading in `dotnet test`.** `dotnet test`
  runs under `testhost.exe` as the entry assembly, so
  `Host.CreateApplicationBuilder()` does NOT pick up the secrets store
  bound to your test project's `UserSecretsId`. The factory must call
  `builder.Configuration.AddUserSecrets(typeof(...).Assembly, optional: true)`
  explicitly. Without it the secret is set on disk but the friendly NRE
  still fires.
- **`services.ai.azure.com` hostname strips dashes.** Resource
  `foundry-abc` resolves to host `foundryabc.services.ai.azure.com` (no
  dash). Authoritative endpoints are in
  `az cognitiveservices account show -n <name> -g <rg> --query properties.endpoints`.
  Use the `AI Foundry API` or `Azure AI Model Inference API` entries —
  not the legacy `properties.endpoint` value, which points at the
  `cognitiveservices.azure.com` hostname that 404s for the `/models` route.
- **Key-based auth disabled (`403`).** Foundry resources provisioned by
  Aspire/azd usually set `disableLocalAuth=true`. Drop the `Key=`
  segment from the connection string and rely on `DefaultAzureCredential`
  (`az login` + a Cognitive Services User role assignment on the
  resource). The Aspire `AddAzureChatCompletionsClient` registration
  picks the credential automatically when the key is absent.
- **Reasoning models (gpt-5, gpt-5-mini, o1, o3) reject `max_tokens`.**
  The Azure.AI.Inference SDK still sends `max_tokens`; reasoning models
  require `max_completion_tokens` and return 400
  `unsupported_parameter`. **The MEAI Quality evaluators swallow the
  400 and record it as a per-metric error row, so tests pass but every
  Quality column is an error.** Pick a non-reasoning judge model
  (gpt-4o, gpt-4o-mini, gpt-4-turbo). Tip: when picking a Foundry
  deployment to point `ConnectionStrings:<alias>` at, check
  `az cognitiveservices account deployment list -n <name> -g <rg>
  --query "[].{name:name, model:properties.model.name}" -o tsv`
  and avoid any deployment whose model is `gpt-5*` or `o*`. When the
  agent uses a reasoning model in production, set
  `EVAL_JUDGE_DEPLOYMENT_NAME=<non-reasoning-alias>` so the judge
  client points at a compatible deployment while the agent client
  keeps the production model.

## Evaluators

- **Wiring 4 separate safety evaluators.** Use `ContentHarmEvaluator`
  for the Hate / SelfHarm / Violence / Sexual bundle — single Foundry
  call, 4 metrics back. The 4 individual evaluators
  (`HateAndUnfairnessEvaluator` etc.) are a strict subset.
- **Putting `RelevanceTruthAndCompletenessEvaluator` in the default
  set.** Marked experimental upstream; not part of the v2 default.
- **Forgetting `EvaluationContext` for NLP evaluators.** BLEU / GLEU
  need `BLEUEvaluatorContext(IEnumerable<string>)`; F1 needs
  `F1EvaluatorContext(string)`. If `golden.json` lacks
  `reference_response`, NLP evaluators emit `(no reference)` and the
  scenario shows blanks in the report.

### Tuning Quality for stylistic agents

The built-in Quality evaluators (`RelevanceEvaluator`,
`CoherenceEvaluator`, `FluencyEvaluator`, `CompletenessEvaluator`,
`EquivalenceEvaluator`) use **generic rubrics baked into the
evaluator's judge prompt**. They don't know about *your* agent's
contract.

- **`CompletenessEvaluator` explicitly rewards thoroughness.** Any
  agent whose contract is "compress / summarize / dumb-down" (ELI5,
  TL;DR summarizers, tweet-length responders, structured-extractor
  agents) will score 1-2 even when working perfectly. The score is
  measuring conformance to a generic "answer thoroughly" rubric, not
  conformance to your agent's contract.
- **`EquivalenceEvaluator` rewards lexical/semantic match to a
  reference.** If your `golden.json` references are full-form
  explanations but your agent emits paraphrases or stylized variants,
  Equivalence will flag drift that isn't a real regression.
- **`CoherenceEvaluator` and `FluencyEvaluator`** penalize
  fragmentary / one-line / heavily-structured outputs (JSON-only,
  bullet-only, single-sentence). Agents that respond in a strict
  format will be marked down.
- **Only `RelevanceEvaluator` is largely intent-agnostic** — it
  checks "did the response address the query" without preferring
  long-form. It's the one Quality metric that's mostly safe to leave
  on for any agent.

**Three remediation patterns, in increasing order of effort:**

1. **Drop the offending evaluators per app.** Edit
   `Reporting/ReportingConfig.cs` and remove `CompletenessEvaluator`
   / `EquivalenceEvaluator` from the judge-tier evaluator list.
   Keep Relevance + Coherence + Fluency. Document the choice in a
   `// per-app: ELI5 contract → drop Completeness` comment so future
   re-scaffolds don't add them back.
2. **Rewrite goldens in the agent's voice.** Edit `Quality/golden.json`
   so `reference_response` is itself ELI5-style (or tweet-style, or
   bullet-style). Equivalence becomes meaningful again; Completeness
   still complains.
3. **Add a custom rubric-driven evaluator.** Write a `RubricEvaluator`
   that reads `Quality/rubric.md` and asks the judge to score the
   response against *your* criteria ("Is the explanation appropriate
   for a 5-year-old? Score 1-5."). See
   `references/evaluators-catalog.md#custom-rubric-driven-evaluator`
   for the template. This is the right long-term answer for any
   non-generic agent.

> **Surface this in the chat output on first scaffold.** The user
> shouldn't have to interpret bad Quality scores against a bad-fit
> rubric and conclude their agent is broken. Step 11 of `SKILL.md`
> calls this out explicitly in the Quality headline block.

## Configuration

- **Hard-coding a price table.** Lives in `Telemetry/prices.json`,
  user-editable. Costs change.
- **Auto-failing the build on quality regressions.** Quality mode is
  informational by default. Users opt into a hard-fail by editing
  `quality.thresholds.json` (which maps to real MEAI metric names like
  `Relevance` / `BLEU` / `F1` and `EvaluationRating` levels), then
  setting `hard_fail: true`.
- **Wrong `Microsoft.Extensions.Hosting` version.** Must be `10.0.1`
  (not `10.0.0`) to satisfy the transitive constraint from
  `Microsoft.Agents.AI.Hosting` 1.x. Pinning `10.0.0` produces
  `NU1605` (treated as error in the agentic-app project graph).
- **Forgetting `.gitignore` entries.** Must include both
  `.copilot/perf-reports/evals/` and `<App>.Evals.Tests/_store/`.
  Otherwise reports pollute history and the persistent `_store/`
  blocks PR diffs.

## Update mode

- **Overwriting `Quality/rubric.md` or `Quality/golden.json`.** These
  are user data — never overwrite. The skill's update-mode behaviour
  table in step 1a of `SKILL.md` is the source of truth.
- **Migrating `golden.json` schema destructively.** Migration is
  additive only: new fields go in as nullable, existing rows are
  preserved.
