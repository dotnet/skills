// report.mjs — finding model, [SEVERITY]/PROOF/WHY/FIX formatting, and verdict.
//
// A Finding is the atomic output of every grader. Severities follow the review
// checklist: BLOCKER > MAJOR > MINOR > INFO. INFO never affects the verdict and
// never fails the gate.
//
// Evidence-based severity rule (per cross-family review): a MAJOR/BLOCKER MUST
// carry a concrete proof (quoted file:line text). `finding()` enforces this —
// a blocking severity with no proof is automatically downgraded to MINOR.

export const SEVERITY = Object.freeze({
  BLOCKER: "BLOCKER",
  MAJOR: "MAJOR",
  MINOR: "MINOR",
  INFO: "INFO",
});

const SEVERITY_RANK = { BLOCKER: 3, MAJOR: 2, MINOR: 1, INFO: 0 };

// Severities that fail the gate / flip a Vally grader to passed:false.
export function isBlocking(severity) {
  return severity === SEVERITY.BLOCKER || severity === SEVERITY.MAJOR;
}

/**
 * Build a finding. Enforces the evidence rule: a BLOCKER/MAJOR without a proof
 * string is downgraded to MINOR so we never fail a build on an unproven claim.
 *
 * @param {object} f
 * @param {string} f.severity   one of SEVERITY.*
 * @param {string} f.dimension  short dimension id, e.g. "assertion-hardening"
 * @param {string} f.file       repo-relative file path (proof location)
 * @param {number|null} [f.line] 1-based line number, or null
 * @param {string} [f.proof]    exact quoted text at file:line
 * @param {string} f.why        why this matters
 * @param {string} f.fix        concrete suggested fix
 * @param {string} [f.rule]     checklist rule id, e.g. "B1"
 */
export function finding(f) {
  let severity = f.severity;
  const hasProof = typeof f.proof === "string" && f.proof.trim().length > 0;
  if (isBlocking(severity) && !hasProof) {
    severity = SEVERITY.MINOR;
  }
  return {
    severity,
    dimension: f.dimension,
    file: f.file ?? null,
    line: f.line ?? null,
    proof: hasProof ? f.proof.trim() : null,
    why: f.why ?? "",
    fix: f.fix ?? "",
    rule: f.rule ?? null,
  };
}

/** 1-based line number of the first line containing `needle`, or null. */
export function lineOf(text, needle) {
  if (!text || !needle) return null;
  const lines = text.split("\n");
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].includes(needle)) return i + 1;
  }
  return null;
}

/** The raw text of a given 1-based line, trimmed, or "". */
export function textOfLine(text, line) {
  if (!text || !line) return "";
  const lines = text.split("\n");
  return (lines[line - 1] ?? "").trim();
}

/** Highest severity present in a set of findings (INFO if empty). */
export function maxSeverity(findings) {
  let max = SEVERITY.INFO;
  for (const f of findings) {
    if (SEVERITY_RANK[f.severity] > SEVERITY_RANK[max]) max = f.severity;
  }
  return max;
}

/**
 * Verdict from findings (matches the checklist verdict axis):
 *   Rework        — any BLOCKER open.
 *   Fix-then-ship — any MAJOR or MINOR open (no BLOCKER).
 *   Ship          — no BLOCKER/MAJOR/MINOR (INFO only, or nothing).
 */
export function verdict(findings) {
  const max = maxSeverity(findings);
  if (max === SEVERITY.BLOCKER) return "Rework";
  if (max === SEVERITY.MAJOR || max === SEVERITY.MINOR) return "Fix-then-ship";
  return "Ship";
}

function locLabel(f) {
  if (!f.file) return "(no location)";
  return f.line ? `${f.file}:${f.line}` : f.file;
}

/** Render a single finding in the [SEVERITY] dimension — file:line shape. */
export function formatFinding(f) {
  const rule = f.rule ? ` (${f.rule})` : "";
  const lines = [`[${f.severity}] ${f.dimension}${rule} — ${locLabel(f)}`];
  if (f.proof) lines.push(`  PROOF: ${JSON.stringify(f.proof)}`);
  if (f.why) lines.push(`  WHY:   ${f.why}`);
  if (f.fix) lines.push(`  FIX:   ${f.fix}`);
  return lines.join("\n");
}

/** Count findings by severity. */
export function severityCounts(findings) {
  const counts = { BLOCKER: 0, MAJOR: 0, MINOR: 0, INFO: 0 };
  for (const f of findings) counts[f.severity]++;
  return counts;
}

/** Render a full report block for one unit. */
export function formatUnitReport(unitLabel, findings) {
  const lines = [`### ${unitLabel}`];
  if (findings.length === 0) {
    lines.push("  (no findings)");
  } else {
    for (const f of findings) lines.push(formatFinding(f));
  }
  lines.push(`  VERDICT: ${verdict(findings)}`);
  return lines.join("\n");
}
