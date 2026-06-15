---
name: configure-agentic-perf-rules
description: >
  Installs or updates an always-on rules block in a .NET agentic app that makes coding
  agents volunteer perf and cost concerns by default — agent count, handoff edges,
  per-agent model selection, message-history strategy, per-turn token cost, and
  post-change measurement. The rules are written into the project's agent-instructions
  file (`.github/copilot-instructions.md` by default) inside a sentinel-delimited managed
  block that is idempotent and version-aware on update.
  USE FOR: a .NET project using Microsoft Agent Framework (`Microsoft.Agents.AI`) with
  Aspire and Microsoft Foundry where the user reports "Copilot doesn't catch perf
  issues", wants up-front guard-rails before adding more agents/handoffs/tools, or is
  scaffolding a new MAF/Aspire/Foundry agentic .NET app.
  DO NOT USE FOR: non-agentic .NET projects (use `optimizing-dotnet-performance`),
  non-.NET agentic projects, auditing existing code (use `audit-agentic-app-perf`), or
  measuring runtime telemetry (use `setup-maf-evals`).
license: MIT
---

# Configure Agentic Perf Rules

This skill writes a managed block of always-on instructions into the target project's
agent-instructions file so coding agents (Copilot, etc.) volunteer agentic-perf concerns
during normal work, instead of waiting to be asked. The block is delimited by sentinel
HTML comments and embeds the skill version so future runs can update it cleanly without
clobbering user-edited threshold values.

## When to Use

- A .NET project uses Microsoft Agent Framework (`Microsoft.Agents.AI`) — typically with
  Aspire (`Aspire.Hosting.*`) and Microsoft Foundry deployments — and the user wants
  default-on perf guidance.
- Scaffolding a new MAF/Aspire/Foundry agentic .NET app and the user wants to start with
  perf guard-rails in place.
- The user reports that the coding agent is not catching perf issues until prompted.
- The user wants to update an existing managed block to a newer version of the rules.

## When Not to Use

- The project is not a .NET agentic app — use `optimizing-dotnet-performance` for general
  .NET performance guidance.
- The user wants the agent to actually audit existing code right now — use
  `audit-agentic-app-perf` instead. This skill only installs guidance.
- The user wants to measure tokens, latency, or quality scores — use `setup-maf-evals`.
- The user wants to pick or change per-agent model assignments — use `select-agent-models`.
- Generic prompt-engineering or non-perf coding-agent rules (keep those in the user's own
  instructions section, outside the managed block).

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Target project root | Yes | Repository root containing the .NET solution |
| Existing instructions file | No | Path to existing `.github/copilot-instructions.md` or `AGENTS.md` if non-default |
| Custom thresholds | No | Per-project override values for agent count, handoff edges, token warn levels |

## Workflow

> **Outcome:** the project's agent-instructions file contains an up-to-date managed
> rules block. Re-running this skill is safe and idempotent.

### Step 1: Locate or create the target instructions file

In priority order, look for:

1. `.github/copilot-instructions.md` (preferred — GitHub Copilot native location).
2. `AGENTS.md` at repository root (cross-tool standard).
3. None of the above — create `.github/copilot-instructions.md`. Create the `.github/`
   directory if it does not exist.

If both `copilot-instructions.md` and `AGENTS.md` exist, the managed block goes in
`copilot-instructions.md` and a one-line stub is written to `AGENTS.md` pointing to it.

### Step 2: Detect any existing managed block

A managed block is delimited by sentinel HTML comments:

```markdown
<!-- BEGIN: managed by configure-agentic-perf-rules vX.Y.Z -->
...
<!-- END: managed by configure-agentic-perf-rules -->
```

Scan the target file. If a managed block is present, parse its version from the
`BEGIN` comment.

| Detected state | Action |
|----------------|--------|
| No block present | Append a new managed block at the end of the file |
| Block present, same version as this skill | No-op — report "already current" and stop |
| Block present, older version | Show a diff to the user; replace the block on confirm; preserve any user-edited threshold values from the existing frontmatter |
| Block present, newer version than this skill | Refuse to downgrade — report version mismatch and stop |

### Step 3: Render the managed block

The managed block has three parts in this exact order:

1. **Sentinel BEGIN comment** with the skill version.
2. **Threshold frontmatter** (a fenced YAML code block) so users can override numeric
   defaults without editing prose. The default values are in
   `references/threshold-defaults.md`.
3. **Six rule sections**, one per category, in the order listed in the next step. Each
   section is short — a "before X, justify Y" lead sentence, the default threshold, and
   one or two crisp expectations. Long-form rationale lives in the reference docs and is
   not duplicated in the project's instructions file.

See `references/managed-block-template.md` for the exact rendered output, including the
threshold frontmatter format and section ordering.

### Step 4: Write the six rule categories

Each rule is in the form **"Before X, justify Y."** Categories, in order:

1. **Agent count.** Before adding a new agent to a workflow, justify why the new
   responsibility cannot be a tool call on an existing agent. Default ceiling: 3 agents
   per workflow.
2. **Handoff edges.** Before adding an LLM-routed handoff edge, justify why a
   deterministic edge or a conditional `WorkflowBuilder` branch will not work. Default
   ceiling: 2 LLM-routed edges traversed per user turn.
3. **Model selection.** Before defaulting to a frontier model (e.g. `gpt-4o`), name the
   agent's role and pick from the role→model matrix in the `select-agent-models` skill.
4. **Message-history strategy.** Before sending the full conversation history to an
   agent, state the bound — turn count, token cap, summarization point, or retrieval
   strategy. Default warning when unbounded full-history is used in a multi-turn workflow.
5. **Token / cost surfacing.** Before implementing a non-trivial change to an agent's
   prompt, tools, or model, estimate per-turn token cost. Default warnings: more than
   8000 input tokens or more than 2000 output tokens projected per turn, or any change
   that adds more than 20% to a measured baseline.
6. **Post-change measurement.** After a non-trivial change to a workflow, propose
   running `setup-maf-evals` (or an existing `.Evals` project) to confirm the change is
   net-positive — or explicitly note why measurement is not warranted.

Long-form rationale, examples, and counter-examples for each rule live in
`references/rule-rationales.md`.

### Step 5: Update the cross-tool stub (if applicable)

If `AGENTS.md` exists alongside `.github/copilot-instructions.md`, ensure `AGENTS.md`
contains (or has appended) a single line:

```markdown
> Agentic-perf rules for this project live in `.github/copilot-instructions.md` (managed by `configure-agentic-perf-rules`).
```

This avoids duplicating the rules across files while keeping cross-tool agents pointed
at the right source.

### Step 6: Commit guidance

If the project is a Git repository and the user wants the change committed, use a single
commit message of the form:

```
Install configure-agentic-perf-rules vX.Y.Z

Adds always-on agentic-perf guidance to .github/copilot-instructions.md.
```

Do not commit on the user's behalf without confirmation.

## Validation

After the skill runs, the agent must verify:

1. **File exists** at the resolved target path.
2. **Sentinel comments present** with matching `BEGIN`/`END` markers and a parseable
   version in the `BEGIN` comment.
3. **Threshold frontmatter parses** as valid YAML (no syntax errors introduced).
4. **All six rule sections** are present, in the canonical order.
5. **Round-trip:** re-running the skill against the now-updated file is a no-op (reports
   "already current"). If a second run produces any modification, the install was not
   idempotent and the skill failed.

If `AGENTS.md` was updated, also confirm the stub line is present exactly once.

## Common Pitfalls

- **Managing user-authored content.** Never modify content outside the sentinel block.
  All edits stay strictly between `BEGIN` and `END` markers.
- **Threshold preservation on update.** When updating to a newer version of the rules,
  preserve any user-edited values in the threshold frontmatter rather than resetting to
  defaults. Diff against the old defaults to detect user edits.
- **Version downgrades.** If the file has a newer skill version than the running skill,
  refuse to overwrite. Tell the user to update the skill before re-running.
- **Sentinel collision.** If a different tool has authored a similar-looking managed
  block (different `BEGIN` text), do not assume it is ours. Match on the exact sentinel
  string `BEGIN: managed by configure-agentic-perf-rules`.
- **Don't re-author rules into the prose.** Keep the SKILL.md body and the project's
  instructions file in sync via `references/managed-block-template.md` — do not paste
  rule prose directly into multiple places.

## References

- `references/managed-block-template.md` — the exact rendered template with sentinels
  and frontmatter.
- `references/threshold-defaults.md` — default numeric values and the rationale for each.
- `references/rule-rationales.md` — long-form prose for each of the six rule categories,
  with examples and counter-examples.
- Companion skills: `audit-agentic-app-perf`, `select-agent-models`, `setup-maf-evals`.
