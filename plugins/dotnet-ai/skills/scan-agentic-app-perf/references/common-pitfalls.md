# Common pitfalls

Real-world failure modes for `scan-agentic-app-perf` — observed during
dogfooding (interview-coach v1/v2, ELI5Agent, behavioral-interview-coach).

## Output discipline

- **Editing source code.** This skill is read-only. The ONLY write
  paths it owns are `.copilot/perf-reports/scan-<ts>.md`,
  `latest-scan.md`, and `check-glossary.md`. Never touch
  `.gitignore`, source files, config files, AppHost, or
  `*.csproj`. If a check tempts you to fix the issue inline, stop and
  add it as a finding instead — the user opts into the fix through
  the step-6 routing prompt.
- **Surfacing more than 3 critical findings in chat.** Step 5 caps
  chat output at 3 critical findings + counts + report path. Anything
  beyond that goes in the report file only. Pasting the entire
  report into chat defeats the "single file you can re-open" UX.
- **Forgetting `latest-scan.md`.** The timestamped report is for
  history; `latest-scan.md` is what tooling and humans will actually
  open first. They must be byte-for-byte identical for the same run.

## Evidence integrity

- **Hallucinating findings.** Every finding must cite a real file
  and (where applicable) a real line. Before adding a finding, re-open
  the cited file and verify the snippet exists at the cited line — that
  is the "evidence gate" in step 3. If you can't point to evidence,
  drop the finding. A 5-finding report with verified citations beats
  a 15-finding report with two hallucinations every time.
- **Citing line ranges without re-reading.** Files drift between
  the inventory pass (step 1) and the per-category checks (step 2).
  Re-read the cited region right before writing each finding, not
  before the entire batch.
- **Absence-of-X findings without searched-pattern evidence.** When
  a finding fires because something is *missing* (e.g. `otel.no-token-cost` "no token
  surfacing"), the `evidence` field MUST list the exact files and
  patterns that were searched — not a snippet. This is what lets the
  user reproduce the negative result.

## False positives we keep seeing

- **`model.hardcoded` firing on AppHost code.** This check is about
  model ids living in *agent service* files (`*.Agent/Program.cs`
  etc.) where swapping requires a code change. Model ids declared in
  the AppHost via `foundry.AddDeployment("chat", FoundryModel.OpenAI.Gpt4oMini)`
  are the **canonical Aspire-native place** — that's not a defect.
  When you encounter this pattern, either suppress `model.hardcoded`
  entirely or downgrade to `info` with a "no action required" `next:`.
- **`model.same-default` on single-agent apps.** The check explicitly
  assumes ≥2 agents (different roles, different needs). Do not fire on
  1-agent apps; the check is trivially "satisfied" with one model.
- **`otel.no-token-cost` when `Microsoft.Extensions.AI` activity
  source is registered.** MEAI emits `gen_ai.usage.*` activity tags
  automatically; if `AddSource("Microsoft.Extensions.AI")` is wired in
  OTel and an OTLP exporter is configured, this check should NOT
  fire. The check is for codebases that strip the source or wrap MEAI
  behind custom infrastructure that loses the tags.

## Per-check sharpening

- **`prompt.oversized`.** Use a rough token estimator (chars/4) or
  `cl100k_base` if available. Never claim an exact token count without
  naming the encoder you used.
- **`tools.duplicate`.** Compare tool *descriptions* (the
  `[Description("...")]` attribute), not method names. Two tools with
  the same method name but different descriptions are usually fine;
  two tools with different names and a copy-pasted description are
  usually a refactor candidate.

## Routing offer (step 6)

- **Inferring intent from the original prompt.** Even if the user
  said "audit and fix it", this skill's job ends at the routing
  prompt. The follow-up skill is responsible for its own
  diff-and-confirm flow. Never call into `configure-agentic-perf-rules`
  (apply mode) without the user explicitly
  picking that letter at the prompt.
- **Listing routes for skills not actually referenced.** Step 6 says
  to render only letters whose target skill is named in some
  finding's `ref:` field. If no finding routed to `setup-maf-evals`,
  do not offer it. Empty routing offer = skip the prompt entirely.

## Inventory edge cases

- **Multiple AppHost projects.** If a solution has more than one
  `*.AppHost.csproj`, scan each one and emit one report per host
  with the host name in the filename
  (`scan-<host>-<ts>.md`). Do NOT merge — the topology, model set,
  and OTel wiring belong to each host independently.
- **Agent registered via DI lambda without `AddAIAgent`.** Patterns
  like `services.AddSingleton<IAgent>(...)` or custom factories also
  count as agents. The detection in step 1 must include any path
  that ends up registering an `IAgent` / `AIAgent` in the host's
  service provider.
