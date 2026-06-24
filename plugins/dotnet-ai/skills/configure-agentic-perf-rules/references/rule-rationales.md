# Rule Rationales

Long-form rationale, examples, and counter-examples for each of the six rules in the
managed block. The managed block itself is intentionally terse; this file is the
"why" behind each rule, used by the agent when explaining a violation to the user.

---

## 1. Agent count

**Rule.** Before adding a new agent to a workflow, justify why the new responsibility
cannot be a tool call on an existing agent. **No hard ceiling** — the answer "this needs
a clearly different system prompt, toolset, or output style" is a valid justification.

**Why it matters.** Each additional agent multiplies the routing surface area (the LLM
has to decide whether *this* turn should go to *that* agent) AND inflates per-turn
input-token cost: every agent's system prompt + tool descriptions are paid on every
turn the agent participates in. Decision quality degrades as choices grow.

**When a new agent is justified.** The new responsibility involves a meaningfully
different *system prompt*, *toolset*, or *output style* that would muddy the existing
agent's instructions if folded in. Examples:

- **Yes, new agent:** A "Coder" agent that writes code and a "Reviewer" agent that
  critiques code — clearly different roles, different prompts.
- **No, just a tool:** A "Database Reader" agent that looks up rows. This is a tool on
  whatever agent needs the data, not its own agent.
- **No, just a tool:** A "Formatter" agent that reformats output. Again, a tool.

**On thresholds.** Earlier versions of this rule had an `agent_count_max: 3` numeric
ceiling. It was removed because legitimate designs (e.g. specialist-handoff
interview/coaching workflows with 4-5 well-scoped agents) tripped it as often as
genuine bloat did. The justify-the-add gate is the actual mechanism; a number was
illusory precision.

---

## 2. Handoff edges

**Rule.** Before adding an LLM-routed handoff edge, justify why a deterministic edge or
a conditional `WorkflowBuilder` branch will not work. **No hard ceiling.**

**Why it matters.** Every LLM-routed edge is an additional LLM call before the user
gets a response. Deterministic routing — "after Coach runs, always return to
Interviewer" expressed as a `WorkflowBuilder` edge — costs zero extra latency and zero
extra tokens. Free-form LLM routing also amplifies decision-quality variance.

**When LLM routing is justified.** The decision genuinely requires reading the user's
intent — for example, "is this answer detailed enough to grade?" or "which specialist
domain does this question belong to?". When the decision is mechanical ("after coach
runs, always go back to interviewer"), use a deterministic edge.

**Concrete pattern (good):**
- Interviewer ↔ Coach with one LLM-routed edge from Interviewer ("ready to grade?")
  and a deterministic edge back from Coach (always returns to Interviewer for the next
  question).

**Concrete pattern (bad):**
- Five specialists in a fully-connected handoff graph where every transition is
  LLM-routed. Symptom: Copilot constantly proposes new edges as the workflow grows.

**On thresholds.** Earlier versions of this rule had an
`llm_routed_edges_max_per_turn: 2` numeric ceiling. Same reason as rule #1: the gate
is the justification, not the number. A 4-hop deterministic chain with one LLM-routed
intent decision is fine; two LLM-routed edges per turn in a misdesigned graph is not.

---

## 3. Model selection

**Rule.** Before defaulting to a frontier model (e.g. `gpt-4o`), name the agent's role
and pick from the table below. If unsure which role applies, **stop and ask the user**
— do not silently default to `gpt-4o`.

**Why it matters.** A frontier model on a router/triage agent costs roughly 10x more
per token than `gpt-4o-mini` and is often *worse* at the routing job (frontier models
are tuned for nuanced generation, not cheap classification). The default-everything-to-
gpt-4o pattern is the largest single source of unnecessary spend in agentic apps.

**Role → model class:**

| Role                                | Pick                                                  | Why                                                   |
|-------------------------------------|-------------------------------------------------------|--------------------------------------------------------|
| Router / triage / "is this done?"   | small-fast (`gpt-4o-mini`, current cheap-fast id)     | Classification, not generation; latency dominates      |
| Validator / scorer / structured JSON| small-fast + JSON mode + low temp                     | Deterministic output; cache-friendly                   |
| Formatter (Markdown / JSON shape)   | small-fast, pinned                                    | Output stability matters more than peak quality        |
| Worker / summarizer / extraction    | small-fast, **or** Foundry `model-router` if prompt length varies | Most calls happen here; latency dominates    |
| Planner / decomposer / reasoning    | reasoning-class (`o4-mini`, current reasoning id)     | Output drives N downstream calls; quality matters most |
| Creative / nuanced generation       | frontier (`gpt-4o`, current frontier id)              | Genuinely needs frontier capability                    |

**When frontier is justified.** The agent's job is genuinely creative or nuanced
generation, or it must follow complex instructions reliably. Routers, validators,
formatters, and most workers almost never fall in this bucket.

**Specific model ids age fast.** The table above uses `gpt-4o-mini`, `o4-mini`, and
`gpt-4o` as anchor examples. Before pinning, check your Foundry catalog
(https://learn.microsoft.com/azure/foundry/openai/concepts/models) for the current
cheap-fast, reasoning-class, and frontier ids. Foundry's `model-router` deployment is
the recommended pick whenever the prompt length or complexity genuinely varies per
request and you don't need cache stability (typical: worker tier).

**State the why in code.** When you pick a frontier or reasoning model, leave a
one-line comment naming the role and why the cheaper tier wouldn't work. This makes
the choice auditable and the next person can challenge it.

---

## 4. Message-history strategy

**Rule.** Before sending the full conversation history to an agent, state the bound —
turn count, token cap, summarization point, or retrieval strategy.

**Why it matters.** Per-turn token cost grows linearly in history length. A workflow
that sends 50 turns of history into every LLM call is paying for 50 turns of attention
on every single response. This is the most common token-bloat pattern in handoff-style
workflows, where every agent in the chain re-sees everything.

**Acceptable bounds (in order of preference):**

1. **Sliding window:** keep only the last N turns. Cheap, simple, bounded.
2. **Summarization checkpoint:** every M turns, replace the oldest portion with a
   summary. Preserves long-range context with a fixed-size payload.
3. **Retrieval / per-agent context:** each agent receives only the messages relevant
   to its role, not the full transcript. Highest engineering cost; biggest savings.

**When unbounded full history is justified.** Single-shot interactions (no multi-turn
loop), or workflows where the entire history is short by construction (e.g. always
under 2K tokens).

---

## 5. Token / cost surfacing

**Rule.** Before implementing a non-trivial change to an agent's prompt, tools, or
model, estimate per-turn token cost. Default warnings: more than 8000 input tokens or
2000 output tokens per turn, or any change that adds more than 20% to a measured
baseline.

**Why it matters.** The window between "looks fine in dev" and "$5K/month surprise" is
measured in token counts, and most contributors do not look at token counts at dev time
unless something is on fire. Surfacing projected token cost *before* implementation
catches the bloat at the cheapest possible moment.

**How to estimate.** Token count is roughly characters / 4 for English prose;
structured output is denser. For prompt changes, count the new prompt body. For tool
additions, count the schema + description per tool * expected frequency. For model
swaps, multiply by the new model's per-token price ratio.

**When to skip.** Cosmetic changes that do not alter prompt or tool schema (e.g.
renaming a class). Changes to non-LLM code paths.

---

## 6. Post-change measurement

**Rule.** After a non-trivial change to a workflow, propose running `setup-maf-evals`
(or an existing `.Evals` project) to confirm the change is net-positive — or explicitly
note why measurement is not warranted.

**Why it matters.** "Trial and error" is the default tuning loop in agentic apps and
it produces survivor-bias outcomes — changes that *seemed* better are kept; changes
whose downsides did not surface in the first few hand-tested turns get baked in. A
small eval suite (3-10 scenarios) closes this loop with data.

**Trigger conditions.** Any of the following count as a "non-trivial change" warranting
a measurement proposal:

- Adding or removing an agent
- Adding or rewiring a handoff edge
- Swapping a model
- Substantially rewriting an agent's instructions
- Adding or substantially modifying a tool

**When skipping is fine.** Cosmetic refactors. Test-only changes. Changes that the
existing eval suite already covers — in that case, the proposal is to *run* the suite,
not to author new scenarios.
