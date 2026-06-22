# Common pitfalls

Real-world failure modes for `select-agent-models` — observed during
dogfooding (interview-coach v2, behavioral coach, ELI5Agent abort path).

## Applicability

- **Running on single-agent apps.** Step 1 requires ≥2 agents.
  Single-agent apps trivially don't benefit — there's no role
  diversity to optimize across. Abort cleanly with a chat message
  ("ELI5Agent has 1 agent — `select-agent-models` does not apply.
  Use `scan-agentic-app-perf` for a single-agent check"). Do NOT
  write a model-plan file.
- **Running on apps without distinct model literals.** If every
  agent already resolves the same `IChatClient` from DI (Aspire's
  default), there's no per-agent override surface to recommend
  against. Still produce a plan — but mark the apply mode as
  "requires opting into per-agent `AddChatClient(<alias>)` first".

## Role classification

- **Forcing a role on every agent.** When an agent's instructions
  and tool list don't cleanly map to one of the seven roles, mark
  the role as `unclear` and explicitly ask the user. Do not silently
  default to "worker" — that produces wrong recommendations the user
  doesn't realize are wrong.
- **Classifying by agent name alone.** Names lie. `"PlannerAgent"`
  might actually be a router; `"FastWorker"` might be doing planning.
  Classify by:
  1. The system prompt's verbs (decompose / pick / validate / format / summarize),
  2. The tool list (no tools → not a worker),
  3. The handoff position (root agent → likely router or planner).
- **Treating multi-role agents as one.** If an agent is doing both
  planning and validation, surface it as "role: unclear — appears
  to mix planning and validation; consider splitting". Don't pick
  one role and ignore the other.

## Apply mode

- **Applying without a diff preview.** "Apply" mode MUST show a
  unified diff of the changes (AppHost connection-string updates +
  per-agent `IChatClient` registration changes) AND wait for explicit
  user confirmation before any write. The diff is the user's last
  chance to catch a wrong role classification before it ships.
- **Editing files outside `*.AppHost/` and `*.Agent*/`.** The only
  files this skill edits in apply mode are AppHost code (deployment
  declarations + connection strings) and agent service code (the
  `AddChatClient` call site). `appsettings.json` model overrides are
  an advanced opt-in, not a default write target.
- **Skipping the rollback hint.** After a successful apply, the chat
  output must include "Revert with `git checkout -- <file1> <file2>`"
  naming the exact files touched. Apply mode is a destructive
  operation; the user needs to know the undo path.

## Plan output

- **Recommending downgrades without an eval gate.** Any `delta:
  downgrade` row should have a `risks` line that explicitly says
  "validate via `setup-maf-evals` quality mode before shipping".
  Downgrades that look free on paper often regress task quality.
- **Net cost / latency arrows that don't match the rows.** If the
  plan has 1 upgrade and 3 downgrades, the aggregate "net cost"
  arrow can still go up depending on call frequency — don't just
  count rows. State the assumption ("assumes uniform per-turn call
  frequency across agents") if you can't measure it.

## Cross-skill routing

- **Telling the user to run `scan-agentic-app-perf` for findings
  this skill should produce.** Model-mismatch findings (MA1-MA4 in
  scan-agentic-app-perf) overlap with this skill's recommendations.
  When invoked directly, produce the plan; don't punt to the scan
  skill except when the inventory step fails (no agents detected
  at all).

## Role-model matrix maintenance

- **Recommending a deprecated model id.** The matrix in
  `references/role-model-matrix.md` must be checked against current
  provider availability before each release. `gpt-3.5-turbo` and
  `gpt-4-turbo-preview` are common stale recommendations — prefer
  `gpt-4o-mini` and `gpt-4o` respectively for the same role tiers.
- **Recommending models the user's deployment doesn't have.** When
  the AppHost's Foundry deployments are `chat` (= gpt-4o-mini), the
  plan can recommend `gpt-4o` but must flag "requires adding a new
  deployment in AppHost — see `setup-maf-evals` for the pattern".
