// meta-commentary.mjs — CHECKLIST D5: no provenance / meta-commentary in shipped
// SKILL.md or references.
//
// Inverted per the cross-family review: we flag *unsupported empirical claims*
// and authoring provenance that leaked into shipped text ("learned from
// telemetry", "grounded in N PRs", "in our experiments"). We NEVER require
// provenance phrasing — the goal is to strip authoring asides, not reward them.

import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { finding, SEVERITY } from "../lib/report.mjs";

export const metadata = {
  name: "meta-commentary",
  description: "Flags authoring provenance / unsupported empirical claims left in shipped SKILL.md or references.",
  behavior: { execution: "single" },
  determinism: "static",
  portability: "t1-universal",
  reference: "reference-free",
  temporalScope: "point-in-time",
  costProfile: "free",
};

const DIMENSION = "meta-commentary";

// Strong provenance leaks (MAJOR) — clearly authoring/telemetry asides.
const STRONG = [
  { rule: "D5", re: /learned from telemetry/i, why: "Shipped text reveals the skill was reverse-engineered from telemetry — authoring provenance that doesn't belong in guidance." },
  { rule: "D5", re: /grounded in\s+\d+/i, why: "Empirical provenance claim ('grounded in N ...') leaked into shipped guidance." },
  { rule: "D5", re: /based on .{0,25}\d+\s+(real\s+|merged\s+)*(prs?|pull requests?|commits?)/i, why: "Unsupported empirical provenance ('based on N PRs/commits') in shipped text." },
  { rule: "D5", re: /derived from (analy(z|s)ing|analysis of)\s+\d+/i, why: "Authoring provenance ('derived from analyzing N ...') leaked into shipped guidance." },
];

// Softer author asides / scope claims (MINOR).
const SOFT = [
  { rule: "D5", re: /\bin (our|my) (experiments?|testing|telemetry|dataset)\b/i, why: "Author aside about experiments/telemetry in shipped guidance." },
  { rule: "D5", re: /\bwe (found|observed|noticed|saw|discovered) that\b/i, why: "First-person authoring aside in shipped guidance." },
  { rule: "D5", re: /\b(across|from)\s+\d+\s+(real\s+)?(merged\s+)?(prs?|pull requests?|repos|repositories)\b/i, why: "Empirical scope claim ('across N repos/PRs') presented without support." },
];

function readText(p) {
  try {
    return readFileSync(p, "utf-8");
  } catch {
    return null;
  }
}

// Collect the skill/agent doc plus reference markdown that ships with it.
function docPaths(unit) {
  const paths = [];
  if (unit.skillDocAbs) paths.push(unit.skillDocAbs);
  if (unit.kind === "skill" && unit.skillDir) {
    const skillDirAbs = join(unit.repoRoot, ...unit.skillDir.split("/"));
    for (const p of walkMarkdown(skillDirAbs)) {
      if (p !== unit.skillDocAbs) paths.push(p);
    }
  }
  return paths;
}

function walkMarkdown(dir, acc = []) {
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return acc;
  }
  for (const e of entries) {
    const full = join(dir, e.name);
    if (e.isDirectory()) walkMarkdown(full, acc);
    else if (e.isFile() && /\.md$/i.test(e.name)) acc.push(full);
  }
  return acc;
}

function review(unit) {
  const findings = [];
  const repoRoot = unit.repoRoot ?? process.cwd();
  for (const abs of docPaths(unit)) {
    const text = readText(abs);
    if (text == null) continue;
    const rel = relative(repoRoot, abs).split(sep).join("/");
    const lines = text.split("\n");
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      if (/eval-review-ignore/i.test(line)) continue;
      for (const p of STRONG) {
        if (p.re.test(line)) {
          findings.push(mk(SEVERITY.MAJOR, p, rel, i + 1, line));
          break;
        }
      }
      for (const p of SOFT) {
        if (p.re.test(line)) {
          findings.push(mk(SEVERITY.MINOR, p, rel, i + 1, line));
          break;
        }
      }
    }
  }
  return findings;
}

function mk(severity, p, file, line, raw) {
  return finding({
    severity,
    dimension: DIMENSION,
    rule: p.rule,
    file,
    line,
    proof: raw.trim(),
    why: p.why,
    fix: "Remove the authoring/provenance aside. Keep the guidance itself; state the rule, not how it was derived.",
  });
}

export { review };
