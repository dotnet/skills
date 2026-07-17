// negative-scenario.mjs — CHECKLIST D4: a real "DO NOT USE FOR" must be backed
// by a non-activation eval scenario.
//
// If the skill/agent doc declares an exclusion ("DO NOT USE FOR ...") but NO
// mapped eval scenario asserts non-activation (expect_activation: false), the
// exclusion is untested -> MAJOR. Searches ALL eval files mapped to the unit.

import { finding, SEVERITY } from "../lib/report.mjs";

export const metadata = {
  name: "negative-scenario",
  description: "Requires a non-activation eval scenario when the skill declares a DO NOT USE FOR exclusion.",
  behavior: { execution: "single" },
  determinism: "static",
  portability: "t1-universal",
  reference: "reference-free",
  temporalScope: "point-in-time",
  costProfile: "free",
};

const DIMENSION = "negative-scenario";

// Match a genuine exclusion clause. Require the "for" to avoid matching prose
// like "do not use the deprecated API".
const DO_NOT_USE_FOR = /\bDO NOT USE FOR\b/i;

// An explicit handoff to a *sibling skill* (kebab-case name after "use ...").
// When present, misrouting has real cost (the wrong tool/flow runs), so an
// untested boundary is MAJOR. Otherwise a missing non-activation scenario is a
// coverage gap -> MINOR. (v2: match the negative scenario to the exclusion's
// subject instead of accepting any expect_activation:false anywhere.)
const SIBLING_HANDOFF = /\buse\s+[`'"]?[a-z][a-z0-9]*(?:-[a-z0-9]+)+\b/i;

function findExclusion(text) {
  if (!text) return null;
  const lines = text.split("\n");
  for (let i = 0; i < lines.length; i++) {
    if (DO_NOT_USE_FOR.test(lines[i])) {
      // Include up to two following lines: exclusion clauses often wrap.
      const context = [lines[i], lines[i + 1] ?? "", lines[i + 2] ?? ""].join(" ");
      return { line: i + 1, text: lines[i].trim(), context };
    }
  }
  return null;
}

function review(unit) {
  const findings = [];
  if (!unit.skillDocText) return findings; // nothing to assert against

  const exclusion = findExclusion(unit.skillDocText);
  if (!exclusion) return findings; // no exclusion declared -> N/A

  // Any non-activation scenario anywhere in the mapped evals satisfies D4.
  const hasNegative = unit.evalFiles.some((ef) =>
    ef.parsed.scenarios.some((sc) => sc.hasExpectActivationFalse),
  );

  if (hasNegative) return findings;

  // No eval files at all -> can't test; report INFO rather than a failing sev.
  if (unit.evalFiles.length === 0) {
    findings.push(
      finding({
        severity: SEVERITY.INFO,
        dimension: DIMENSION,
        rule: "D4",
        file: unit.skillDoc,
        line: exclusion.line,
        proof: exclusion.text,
        why: "Skill declares an exclusion but has no eval file, so non-activation can't be verified.",
        fix: "Add an eval with a scenario using expect_activation: false that exercises the excluded case.",
      }),
    );
    return findings;
  }

  // Routing risk drives severity: an explicit "use <sibling-skill>" handoff that
  // is untested can misroute to the wrong tool -> MAJOR. A generic exclusion with
  // no negative scenario is coverage debt -> MINOR (don't dilute MAJOR).
  const routingRisk = SIBLING_HANDOFF.test(exclusion.context);
  const evalList = unit.evalFiles.map((e) => e.path).join(", ");

  findings.push(
    finding({
      severity: routingRisk ? SEVERITY.MAJOR : SEVERITY.MINOR,
      dimension: DIMENSION,
      rule: "D4",
      file: unit.skillDoc,
      line: exclusion.line,
      proof: exclusion.text,
      why: routingRisk
        ? "The exclusion hands off to a sibling skill (\"use <other-skill>\") but no eval scenario asserts non-activation (expect_activation: false). The routing boundary is unenforced — nothing stops this skill from activating on a case that belongs to the other skill."
        : "The skill declares a DO NOT USE FOR exclusion, but no eval scenario asserts non-activation (expect_activation: false). The boundary is untested (coverage gap).",
      fix: `Add a scenario to one of this skill's evals (${evalList}) that uses expect_activation: false and prompts for the excluded use case.`,
    }),
  );
  return findings;
}

export { review };
