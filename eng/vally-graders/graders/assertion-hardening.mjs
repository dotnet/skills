// assertion-hardening.mjs — CHECKLIST B: does the eval verify the RESULT?
//
// Classifies each scenario's intent BEFORE demanding a hard gate (per the
// cross-family review — don't punish advisory/diagnostic evals):
//   - mutating  (edits source: implement/fix/add/tag/refactor/...) -> a hard
//     result gate is REQUIRED; prose/output_* only -> MAJOR (B1/B4).
//   - advisory  (diagnose/explain/review/audit, or "without modifying") -> no
//     gate demanded; we don't flag.
//   - uncertain (neither clear) -> MINOR if no gate (report, don't block hard).
//
// A "hard gate" is run_command_and_assert / file_contains / file_not_contains /
// file_exists / exit_success. output_contains / output_matches / rubric are NOT
// hard gates on their own.

import { finding, SEVERITY } from "../lib/report.mjs";
import { isHardGate } from "../lib/locate.mjs";

export const metadata = {
  name: "assertion-hardening",
  description: "Requires mutating eval scenarios to assert the RESULT (build/test/file gate), not just prose or output matches.",
  behavior: { execution: "single" },
  determinism: "complex-static",
  portability: "t1-universal",
  reference: "reference-free",
  temporalScope: "point-in-time",
  costProfile: "free",
};

const DIMENSION = "assertion-hardening";

// Verbs that imply the agent must EDIT/PRODUCE code, matched only in IMPERATIVE
// position (base form, at a clause boundary or after please/then/now). English
// imperatives use the base form ("generate a class"); descriptive clauses use
// 3rd-person ("the project generates a file") — matching base form only avoids
// misreading a description of the codebase as an instruction to the agent.
const MUT_VERBS =
  "implement|fix|add|create|write|modify|update|refactor|rename|migrate|convert|replace|remove|delete|tag|generate|edit|apply|insert|scaffold|annotate|port|resolve|wire up|fill in|make";
// Verbs that specifically EDIT EXISTING provided files (as opposed to producing
// a new artifact shown in the answer). Only these, when the scenario supplies
// files on disk, earn a MAJOR: a wrong edit to real project files can pass a
// keyword-only assertion. "generate/write/create" produce new content and are
// treated as the lower-severity under-assertion case.
const EDIT_EXISTING =
  /\b(fix|modify|update|refactor|rename|migrate|convert|replace|remove|delete|edit|port|annotate|tag|resolve|upgrade|downgrade|apply)\b/i;
// Request wrappers that precede an agent-directed instruction ("help me fix",
// "can you migrate", "I need you to update"). These let us catch mutating asks
// that aren't at a raw clause boundary while still ignoring 3rd-person
// descriptions of the codebase ("the project generates a file").
const REQUEST =
  "please|can you|could you|would you|help me|i need you to|i want you to|i'?d like you to|i need to|we need to|need to|let'?s|make sure to|ensure";
const IMPERATIVE_MUTATING = new RegExp(
  `(?:^|[.!?\\n]\\s*|["\`]|(?:${REQUEST})\\s+|then\\s+|now\\s+|and\\s+|,\\s+|-\\s+|how do i\\s+|how to\\s+)(?:${MUT_VERBS})\\b`,
  "i",
);

// Verbs whose deliverable is analysis/advice (advisory), not a code change.
const ADVISORY =
  /\b(diagnose|diagnos(e|is|ing)|explain|describe|review|analy(z|s)e|analysis|recommend|compare|report|audit|summar(y|ize|ise)|identify|investigate|symbolicate|assess|evaluate|which|why does|why is|how does|tell me|should i|what should)\b/i;

// Explicit "do not modify" signals force advisory classification.
const NO_MODIFY =
  /\b(without modifying|don'?t modify|do not modify|analysis only|report[- ]only|just give me|do not (edit|change)|without (editing|changing))\b/i;

function classify(prompt) {
  if (!prompt) return "uncertain";
  if (NO_MODIFY.test(prompt)) return "advisory";
  const mutating = IMPERATIVE_MUTATING.test(prompt);
  const advisory = ADVISORY.test(prompt);
  if (mutating && !advisory) return "mutating";
  // A prompt that both instructs a change AND asks for explanation is a
  // "diagnose-and-fix / show-me-and-explain" task. Whether it needs a hard gate
  // depends on whether it edits provided files (decided in review()).
  if (mutating && advisory) return "mixed";
  if (advisory) return "advisory";
  return "uncertain";
}

// The scenario NAME is the author's own one-line intent summary and is a
// stronger signal than the prose prompt (which may quote a user asking for the
// "wrong" thing that the skill should push back on). Prefer an unambiguous name
// signal; otherwise fall back to the prompt.
function classifyScenario(sc) {
  const name = sc?.name ?? "";
  if (name) {
    const nameMutating = IMPERATIVE_MUTATING.test(name);
    const nameAdvisory = ADVISORY.test(name);
    if (nameMutating && !nameAdvisory) return "mutating";
    if (nameAdvisory && !nameMutating) return "advisory";
  }
  return classify(sc?.prompt ?? "");
}

function scenarioLine(evalText, name) {
  if (!evalText || !name) return null;
  const lines = evalText.split("\n");
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].includes(name)) return i + 1;
  }
  return null;
}

function review(unit) {
  const findings = [];

  // Prefer the native eval.yaml (authored source of truth); fall back to any.
  const evalFile =
    unit.evalFiles.find((e) => e.parsed.format === "native") ?? unit.evalFiles[0];
  if (!evalFile) return findings;
  const { parsed } = evalFile;
  const evalText = parsed.text ?? "";

  for (const sc of parsed.scenarios) {
    // Non-activation scenarios: the agent should NOT act, so a result gate is
    // not applicable here (negative-scenario grader owns that dimension).
    if (sc.hasExpectActivationFalse) continue;

    const hardGates = sc.assertionTypes.filter((t) => isHardGate(t));
    const intent = classifyScenario(sc);
    const line = scenarioLine(evalText, sc.name);
    const proof = line ? textAt(evalText, line) : `scenario "${sc.name}"`;
    const typesLabel = sc.assertionTypes.length ? sc.assertionTypes.join(", ") : "none";

    if (hardGates.length > 0) continue; // has a real result gate — good.

    // Does the scenario hand the agent existing project files to edit on disk?
    // If so, an output-only assertion is a genuine, high-severity gap: a wrong
    // edit to the provided files still passes. If instead the agent is asked to
    // *produce* code shown in its answer (no setup files), output matching is
    // under-asserted but lower risk -> MINOR, not MAJOR (don't cry wolf).
    const editsProvidedFiles =
      (Array.isArray(sc.setupFiles) && sc.setupFiles.length > 0) || sc.copyTestFiles === true;
    // Is the agent editing existing provided files (highest risk) vs. producing
    // new content? Only edit-existing on provided files earns a MAJOR.
    const editsExisting =
      EDIT_EXISTING.test(sc.name ?? "") || EDIT_EXISTING.test(sc.prompt ?? "");

    if ((intent === "mutating" || intent === "mixed") && editsProvidedFiles && editsExisting) {
      findings.push(
        finding({
          severity: SEVERITY.MAJOR,
          dimension: DIMENSION,
          rule: "B1",
          file: evalFile.path,
          line,
          proof,
          why: `Scenario sets up project files and asks the agent to change them, but only has weak assertions (${typesLabel}) and/or a prose rubric. A wrong or half-done edit to the provided files can still pass — the eval doesn't verify the RESULT.`,
          fix: "Add a hard gate: run_command_and_assert (dotnet build / dotnet test) and/or file_contains / file_not_contains asserting the edited file's shape.",
        }),
      );
    } else if (intent === "mutating" || intent === "mixed") {
      findings.push(
        finding({
          severity: SEVERITY.MINOR,
          dimension: DIMENSION,
          rule: "B4",
          file: evalFile.path,
          line,
          proof,
          why: `Scenario asks the agent to produce code but only asserts on output text (${typesLabel}). Keyword matches can pass on code that does not compile or is subtly wrong (checklist B4: assert the RESULT, not the presence of keywords).`,
          fix: "Have the eval write the produced code to a file and gate it: run_command_and_assert (dotnet build) or file_contains on the compiled/expected shape, instead of relying on output_contains alone.",
        }),
      );
    } else if (intent === "uncertain") {
      findings.push(
        finding({
          severity: SEVERITY.MINOR,
          dimension: DIMENSION,
          rule: "B1",
          file: evalFile.path,
          line,
          proof,
          why: `Scenario intent is ambiguous and it has no hard result gate (assertions: ${typesLabel}). If it expects a code change, it can be passed without producing a correct one.`,
          fix: "If the scenario mutates code, add run_command_and_assert / file_contains. If it is advisory, tighten output_matches to a specific domain fact so it isn't trivially satisfiable.",
        }),
      );
    }
    // advisory + no gate -> intentionally no finding.
    }

    return findings;
}

function textAt(text, line) {
  return (text.split("\n")[line - 1] ?? "").trim();
}

export { review, classify, classifyScenario };
