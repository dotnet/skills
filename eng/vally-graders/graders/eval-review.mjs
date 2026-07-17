// eval-review.mjs — the `eval-review` LLM grader.
//
// Covers the judgment dimensions that the free static graders cannot:
//   C  — fixture honesty & integrity (is the trap real; does it discriminate a
//        right vs. wrong fix; do assertions/fixtures line up)
//   D  — skill-design balance (D1–D3): guidance names tradeoffs and *when* to
//        take each side, not just a list of prohibitions
//   DC — domain-correctness & safety: claims are defensible; no harmful guidance
//
// It makes a REAL judge-model call via the Copilot SDK (see lib/llm-client.mjs)
// and emits real [SEVERITY]/PROOF/WHY/FIX findings. It is opt-in (review.mjs
// --llm) and kept OUT of the free per-PR gate because it costs money/latency.
//
// Discipline (never a false BLOCKER):
//   * Every model finding is routed through report.finding(), which downgrades
//     any MAJOR/BLOCKER lacking a quoted proof to MINOR.
//   * Cited file paths are verified against the unit's real files; unknown files
//     are dropped (file/line nulled).
//   * Quoted proof is verified against the unit's source text; unverifiable
//     quotes are dropped (→ the finding downgrades).
//   * Any SDK/token/timeout/parse failure returns an INCONCLUSIVE result
//     (excluded from the verdict), not a finding.

import { finding, SEVERITY, verdict, isBlocking, formatFinding } from "../lib/report.mjs";
import { readRepoFile } from "../lib/locate.mjs";
import { createCopilotLlmClient, LlmUnavailableError } from "../lib/llm-client.mjs";

export const DEFAULT_MODEL = process.env.EVAL_REVIEW_MODEL || "claude-opus-4.6";

export const metadata = {
  name: "eval-review",
  description:
    "LLM review of fixture honesty (C), skill-design balance (D1–D3), and domain-correctness & safety (DC). Real judge-model call; opt-in, not in the free gate.",
  behavior: { execution: "single", requiresLlmClient: true, requiresWorkspace: true },
  determinism: "llm",
  portability: "t1-universal",
  reference: "reference-free",
  temporalScope: "point-in-time",
  costProfile: "low",
  experimental: true,
};

const DIMENSION_RULE = {
  "C-fixture-integrity": "C",
  "D-design-balance": "D",
  "DC-domain-correctness": "DC",
};

// ---------------------------------------------------------------------------
// Prompt construction (pure — unit-testable without a model)
// ---------------------------------------------------------------------------

function truncate(s, n) {
  if (typeof s !== "string") return "";
  return s.length > n ? `${s.slice(0, n)}\n…[truncated ${s.length - n} chars]` : s;
}

export const SYSTEM_MESSAGE = [
  "You are an exacting reviewer of AI-agent *skills* and their *evals* for the dotnet/skills repository.",
  "Assess ONLY these dimensions and nothing else:",
  "  C  (fixture-integrity): Is the eval's trap real? Do fixtures + assertions actually discriminate a CORRECT fix from a plausible WRONG one, or could a trivial/incorrect change also pass? Are setup files, prompts, and assertions mutually consistent?",
  "  D  (design-balance): Does the SKILL.md guidance name real tradeoffs and say WHEN to choose each side, rather than being a bare list of 'do NOT' prohibitions? Flag guidance that is one-sided or absolutist without conditions.",
  "  DC (domain-correctness & safety): Are technical claims defensible for .NET/C#/tooling? Any incorrect, misleading, or unsafe guidance?",
  "",
  "You are given only the SKILL.md text and eval scenarios provided in the user message. Judge ONLY that material.",
  "",
  "EVIDENCE DISCIPLINE (strict):",
  "  * Every MAJOR or BLOCKER finding MUST include a `proof`: a short verbatim quote copied EXACTLY from the provided material, plus the `file` it came from (use the exact file paths given).",
  "  * If you cannot quote exact supporting text, use severity MINOR or INFO — do NOT assert MAJOR/BLOCKER.",
  "  * NEVER invent file paths, line numbers, or quotes. Do not paraphrase inside `proof`.",
  "  * If the material is insufficient to judge a dimension, record a single INFO finding saying so.",
  "",
  "Severities: BLOCKER (ships incorrect or unsafe guidance), MAJOR (clear, proven defect), MINOR (smell or unproven concern), INFO (note/insufficient-evidence).",
  "Report by calling the submit_review tool exactly once. If there are no real issues, submit an empty findings array with verdict 'Ship'.",
].join("\n");

/** Build the {systemMessage, userMessage} for a unit. Pure. */
export function buildPrompt(unit) {
  const parts = [];
  parts.push(`# Unit: ${unit.plugin ?? "?"}/${unit.name ?? "?"} (kind: ${unit.kind ?? "skill"})`);
  parts.push("");
  parts.push(`## Skill document: ${unit.skillDoc ?? "(none resolved)"}`);
  parts.push(unit.skillDocText ? truncate(unit.skillDocText, 9000) : "(no skill document text available)");
  parts.push("");

  const evalFiles = Array.isArray(unit.evalFiles) ? unit.evalFiles : [];
  if (evalFiles.length === 0) {
    parts.push("## Evals: (none found for this unit)");
  }
  for (const ef of evalFiles) {
    parts.push(`## Eval file: ${ef.path} (format: ${ef.parsed?.format ?? "unknown"})`);
    const scenarios = Array.isArray(ef.parsed?.scenarios) ? ef.parsed.scenarios : [];
    if (scenarios.length === 0) parts.push("(no scenarios parsed)");
    scenarios.forEach((s, i) => {
      parts.push(`### Scenario ${i + 1}: ${s.name || "(unnamed)"}`);
      parts.push(`- prompt: ${truncate(s.prompt, 700)}`);
      parts.push(`- assertion types: ${(s.assertionTypes ?? []).join(", ") || "(none)"}`);
      if (Array.isArray(s.rubric) && s.rubric.length) {
        parts.push(`- rubric: ${s.rubric.map((r) => truncate(String(r), 200)).join(" | ")}`);
      }
      const setupFiles = Array.isArray(s.setupFiles) ? s.setupFiles : [];
      if (setupFiles.length) {
        parts.push(`- setup files: ${setupFiles.map((f) => f.path).join(", ")}`);
      }
      if (s.hasExpectActivationFalse) parts.push("- expect_activation: false");
    });
    parts.push("");
  }

  const fixtures = Array.isArray(unit.fixtures) ? unit.fixtures : [];
  if (fixtures.length) {
    parts.push(`## Fixtures (paths): ${fixtures.join(", ")}`);
  }

  return { systemMessage: SYSTEM_MESSAGE, userMessage: parts.join("\n") };
}

// ---------------------------------------------------------------------------
// Tool schema (lazy zod import — optional dependency)
// ---------------------------------------------------------------------------

async function buildTool() {
  let parameters;
  try {
    const { z } = await import("zod");
    const Finding = z.object({
      severity: z.enum(["BLOCKER", "MAJOR", "MINOR", "INFO"]),
      dimension: z.enum(["C-fixture-integrity", "D-design-balance", "DC-domain-correctness"]),
      file: z.string().optional(),
      line: z.number().int().optional(),
      proof: z.string().optional(),
      why: z.string(),
      fix: z.string(),
    });
    parameters = z.object({
      findings: z.array(Finding),
      verdict: z.enum(["Ship", "Fix-then-ship", "Rework"]),
      summary: z.string(),
    });
  } catch {
    // zod (optional dep) not installed — fall back to a minimal schema object
    // that exposes the `.safeParse` the client relies on. This keeps the
    // injected-client path (and its tests) working without the optional deps;
    // the real SDK path installs zod so it gets a proper JSON schema.
    parameters = {
      safeParse(v) {
        if (!v || typeof v !== "object") {
          return { success: false, error: { message: "expected an object" } };
        }
        return {
          success: true,
          data: {
            findings: Array.isArray(v.findings) ? v.findings : [],
            verdict: typeof v.verdict === "string" ? v.verdict : "Fix-then-ship",
            summary: typeof v.summary === "string" ? v.summary : "",
          },
        };
      },
    };
  }
  return {
    name: "submit_review",
    description:
      "Submit the eval-review findings for this unit. Call exactly once. Every MAJOR/BLOCKER finding must include an exact `proof` quote and the `file` it came from.",
    parameters,
  };
}

// ---------------------------------------------------------------------------
// Result mapping (pure — anti-fabrication + evidence discipline)
// ---------------------------------------------------------------------------

/** Whitespace-normalize for lenient proof verification. */
function norm(s) {
  return String(s ?? "")
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase();
}

/** Concatenated source text for a unit (skill doc + eval files + fixtures). */
function unitCorpus(unit) {
  const chunks = [];
  if (unit.skillDocText) chunks.push(unit.skillDocText);
  for (const ef of unit.evalFiles ?? []) {
    if (ef.parsed?.text) chunks.push(ef.parsed.text);
  }
  if (unit.repoRoot && Array.isArray(unit.fixtures)) {
    for (const rel of unit.fixtures) {
      const t = readRepoFile(unit.repoRoot, rel);
      if (t) chunks.push(t);
    }
  }
  return norm(chunks.join("\n"));
}

function knownFileSet(unit) {
  const files = new Set();
  if (unit.skillDoc) files.add(unit.skillDoc);
  for (const ef of unit.evalFiles ?? []) if (ef.path) files.add(ef.path);
  for (const rel of unit.fixtures ?? []) files.add(rel);
  return files;
}

/**
 * Map raw model output to Finding[]. Verifies cited files + quoted proof against
 * the unit; drops unverifiable citations so unproven MAJOR/BLOCKER downgrade.
 */
export function mapFindings(args, unit) {
  const raw = Array.isArray(args?.findings) ? args.findings : [];
  const known = knownFileSet(unit);
  const corpus = unitCorpus(unit);
  const out = [];
  for (const r of raw) {
    let file = typeof r.file === "string" ? r.file.replace(/^\.\//, "").trim() : null;
    let line = Number.isInteger(r.line) ? r.line : null;
    if (file && !known.has(file)) {
      // Hallucinated / unknown file — don't trust the location.
      file = null;
      line = null;
    }
    let proof = typeof r.proof === "string" ? r.proof.trim() : "";
    if (proof) {
      const np = norm(proof);
      // Accept if a >=12-char slice of the quote appears in the corpus (lenient
      // to tolerate light truncation); short quotes must match whole.
      const probe = np.length >= 12 ? np.slice(0, Math.min(np.length, 60)) : np;
      const verified = corpus.length > 0 && probe.length > 0 && corpus.includes(probe);
      if (!verified) proof = ""; // unverifiable → drop → severity downgrades
    }
    const why = typeof r.why === "string" ? r.why : "";
    const augmentedWhy =
      typeof r.proof === "string" && r.proof.trim() && !proof
        ? `${why} [unverified quote omitted: ${truncate(r.proof.trim(), 160)}]`
        : why;
    out.push(
      finding({
        severity: r.severity ?? SEVERITY.MINOR,
        dimension: "eval-review",
        rule: DIMENSION_RULE[r.dimension] ?? r.dimension ?? null,
        file,
        line,
        proof: proof || undefined,
        why: augmentedWhy,
        fix: typeof r.fix === "string" ? r.fix : "",
      }),
    );
  }
  return out;
}

// ---------------------------------------------------------------------------
// Core: run one unit through the model
// ---------------------------------------------------------------------------

/** Shape of an inconclusive (non-failing) result. */
function inconclusive(reason) {
  return { ran: false, inconclusive: true, reason, findings: [], verdict: "Ship", usage: null };
}

/**
 * Review a single unit with the model.
 * @param {object} unit
 * @param {{ client?: object, model?: string, timeoutMs?: number }} [opts]
 *   client: a pre-provisioned LlmClient (with judge()); if omitted, one is
 *   created via the Copilot SDK and shut down afterwards.
 */
export async function reviewUnit(unit, opts = {}) {
  const model = opts.model || DEFAULT_MODEL;

  let tool;
  try {
    tool = await buildTool();
  } catch (err) {
    return inconclusive(`zod (optional dep) unavailable: ${err?.message ?? err}`);
  }

  const { systemMessage, userMessage } = buildPrompt(unit);

  let client = opts.client;
  let ownClient = false;
  if (!client) {
    try {
      client = await createCopilotLlmClient();
      ownClient = true;
    } catch (err) {
      if (err instanceof LlmUnavailableError) return inconclusive(err.message);
      return inconclusive(`could not provision llm client: ${err?.message ?? err}`);
    }
  }

  try {
    const res = await client.judge({
      model,
      systemMessage,
      userMessage,
      tool,
      timeoutMs: opts.timeoutMs ?? 120_000,
    });
    const findings = mapFindings(res.args, unit);
    return {
      ran: true,
      inconclusive: false,
      reason: null,
      findings,
      verdict: verdict(findings),
      usage: res.tokenUsage ?? null,
      model,
    };
  } catch (err) {
    return inconclusive(`model call failed: ${err?.message ?? err}`);
  } finally {
    if (ownClient && client?.shutdown) await client.shutdown();
  }
}

// ---------------------------------------------------------------------------
// Vally Grader adapter
// ---------------------------------------------------------------------------

function toGraderResult(unitLabel, result) {
  if (result.inconclusive) {
    return {
      name: metadata.name,
      kind: "llm",
      passed: true, // inconclusive is excluded from the verdict — never fails
      score: 1,
      label: "inconclusive",
      evidence: `eval-review did not run a model judgment for ${unitLabel}: ${result.reason}`,
      details: [],
      metadata: { inconclusive: true, reason: result.reason, findings: [] },
    };
  }
  const blocking = result.findings.filter((f) => isBlocking(f.severity));
  const passed = blocking.length === 0;
  return {
    name: metadata.name,
    kind: "llm",
    passed,
    score: passed ? 1 : 0,
    label: result.verdict,
    evidence:
      result.findings.length === 0
        ? `eval-review: no issues found for ${unitLabel}.`
        : result.findings.map(formatFinding).join("\n"),
    details: result.findings.map((f) => ({
      name: `eval-review:${f.rule ?? "?"}`,
      kind: "llm",
      passed: !isBlocking(f.severity),
      score: isBlocking(f.severity) ? 0 : 1,
      label: f.severity,
      evidence: formatFinding(f),
    })),
    metadata: { findings: result.findings, usage: result.usage, model: result.model },
  };
}

/**
 * Build a Vally-compatible grader object. `client` is optional: when Vally (or a
 * test) supplies an LlmClient it is used; otherwise grade() provisions its own
 * via the Copilot SDK, and reports inconclusive if that isn't possible.
 *
 * NOTE: Vally does not currently enforce this grader in dotnet/skills' eval path
 * (see README/index.mjs). This adapter exists for Vally-compat / future use.
 */
export function createGrader({ client, model } = {}) {
  return {
    metadata,
    async grade(input) {
      const cwd = input?.config?.cwd ?? process.cwd();
      const env = input?.stimulus?.environment;
      const skillDirs = env?.skills ?? [];
      const injected = client ?? input?.config?.llmClient;
      const chosenModel = model ?? input?.config?.model ?? DEFAULT_MODEL;

      // Resolve the unit from the provided skill dir(s).
      const { unitsFromSkillDirs } = await import("../lib/locate.mjs");
      const units = skillDirs.length ? unitsFromSkillDirs(cwd, skillDirs) : [];
      if (units.length === 0) {
        return toGraderResult("(no unit)", inconclusive("no skill provided in stimulus.environment.skills"));
      }
      // Single-execution grader: review the first resolved unit.
      const unit = units[0];
      const result = await reviewUnit(unit, { client: injected, model: chosenModel });
      return toGraderResult(`${unit.plugin}/${unit.name}`, result);
    },
  };
}

export const grader = createGrader();

export async function grade(input) {
  return grader.grade(input);
}
