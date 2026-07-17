#!/usr/bin/env node
// review.mjs — standalone skill/eval quality gate.
//
// This is the REAL enforcement path (see README): it runs the free static
// graders over changed (default) or all skill+eval units, prints
// [SEVERITY]/PROOF/WHY/FIX findings, and exits nonzero when a blocking finding
// is present.
//
// Opt-in LLM review: with --llm it ALSO runs the `eval-review` judge-model
// grader (fixture honesty C, design balance D1–D3, domain-correctness DC). That
// path costs money/latency, so it is off by default and is NOT part of the free
// per-PR CI gate. If the model/token/SDK are unavailable it reports the unit as
// inconclusive (never a false failure).
//
// Usage:
//   node review.mjs [--all] [--base <ref>] [--unit <substr>]... \
//     [--report-only <dimension>]... [--fail-on BLOCKER|MAJOR|MINOR] [--json] \
//     [--llm [--model <id>]]
//
// Default (no --all): evaluate only the units touched by the PR/working tree,
// determined from `git diff` against --base (or GITHUB_BASE_REF / main).

import { execFileSync } from "node:child_process";
import { parseArgs } from "node:util";

import { staticModules, llmModule } from "./index.mjs";
import { allUnits, unitsFromChangedFiles } from "./lib/locate.mjs";
import {
  formatUnitReport,
  severityCounts,
  verdict,
  isBlocking,
} from "./lib/report.mjs";

const SEVERITY_RANK = { BLOCKER: 3, MAJOR: 2, MINOR: 1, INFO: 0 };

const { values: opts } = parseArgs({
  options: {
    all: { type: "boolean", default: false },
    base: { type: "string" },
    unit: { type: "string", multiple: true, default: [] },
    "report-only": { type: "string", multiple: true, default: [] },
    "fail-on": { type: "string", default: "MAJOR" },
    "repo-root": { type: "string" },
    json: { type: "boolean", default: false },
    llm: { type: "boolean", default: false },
    model: { type: "string" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

if (opts.help) {
  console.log(`eval-review — skill/eval quality gate (free static graders + opt-in LLM review)

Usage:
  node review.mjs [options]

Options:
  --all                     Evaluate every skill+eval unit in the repo.
  --base <ref>              Git base ref for changed-file detection
                            (default: GITHUB_BASE_REF or 'main').
  --unit <substr>           Only units whose "<plugin>/<name>" contains <substr>
                            (repeatable). Implies scanning all units first.
  --report-only <dim>       Downgrade findings from <dimension> to non-blocking
                            (still printed). Repeatable. e.g. eval-deleading.
  --fail-on <severity>      Minimum blocking severity (BLOCKER|MAJOR|MINOR).
                            Default: MAJOR.
  --repo-root <path>        Repo root (default: git toplevel or cwd).
  --json                    Emit machine-readable JSON instead of text.
  --llm                     ALSO run the eval-review judge-model grader
                            (fixture honesty C, design balance D1-D3,
                            domain-correctness DC). Costs money/latency; needs a
                            Copilot token + @github/copilot-sdk. Off by default;
                            NOT part of the free CI gate.
  --model <id>              Judge model for --llm (default: EVAL_REVIEW_MODEL env
                            or 'claude-opus-4.6').
  --help                    Show this help.`);
  process.exit(0);
}

function repoRoot() {
  if (opts["repo-root"]) return opts["repo-root"];
  try {
    return execFileSync("git", ["rev-parse", "--show-toplevel"], { encoding: "utf-8" }).trim();
  } catch {
    return process.cwd();
  }
}

function isGitHub() {
  return process.env.GITHUB_ACTIONS === "true";
}

function changedFiles(root, base) {
  const files = new Set();
  const run = (args) => {
    try {
      const out = execFileSync("git", args, { cwd: root, encoding: "utf-8" });
      for (const l of out.split("\n")) {
        const t = l.trim();
        if (t) files.add(t);
      }
    } catch {
      /* ignore — best effort */
    }
  };
  // Committed changes since the merge-base with base.
  run(["diff", "--name-only", `${base}...HEAD`]);
  // Uncommitted (working tree) changes, for local runs.
  run(["diff", "--name-only", "HEAD"]);
  run(["diff", "--name-only", "--staged"]);
  return [...files];
}

function selectUnits(root) {
  if (opts.all || opts.unit.length > 0) {
    let units = allUnits(root);
    if (opts.unit.length > 0) {
      units = units.filter((u) =>
        opts.unit.some((s) => `${u.plugin}/${u.name}`.includes(s)),
      );
    }
    return units;
  }
  const base = opts.base ?? process.env.GITHUB_BASE_REF ?? "main";
  const files = changedFiles(root, base);
  return unitsFromChangedFiles(root, files);
}

function ghAnnotate(f) {
  if (!isGitHub()) return;
  const level = isBlocking(f.severity) ? "error" : "warning";
  const loc = f.file ? `file=${f.file}${f.line ? `,line=${f.line}` : ""}` : "";
  const msg = `[${f.dimension}${f.rule ? ` ${f.rule}` : ""}] ${f.why}${f.fix ? ` FIX: ${f.fix}` : ""}`;
  console.log(`::${level} ${loc}::${msg.replace(/\n/g, " ")}`);
}

async function runLlm(units, root) {
  // Provision one client for all units; report inconclusive (never fail) if the
  // SDK/token/runtime aren't available.
  const model = opts.model || llmModule.DEFAULT_MODEL;
  const results = new Map(); // unit -> { findings, inconclusive, reason }
  let client;
  const { createCopilotLlmClient, LlmUnavailableError } = await import("./lib/llm-client.mjs");
  try {
    client = await createCopilotLlmClient();
  } catch (err) {
    const reason =
      err instanceof LlmUnavailableError ? err.message : `could not provision llm client: ${err?.message ?? err}`;
    console.error(`⚠ eval-review --llm inconclusive for all units: ${reason}`);
    for (const unit of units) results.set(unit, { findings: [], inconclusive: true, reason });
    return { results, model, clientReason: reason };
  }
  try {
    for (const unit of units) {
      const r = await llmModule.reviewUnit(unit, { client, model });
      results.set(unit, {
        findings: r.findings ?? [],
        inconclusive: Boolean(r.inconclusive),
        reason: r.reason ?? null,
        usage: r.usage ?? null,
      });
    }
  } finally {
    if (client?.shutdown) await client.shutdown();
  }
  return { results, model, clientReason: null };
}

async function main() {
  const root = repoRoot();
  const reportOnly = new Set(opts["report-only"]);
  const failRank = SEVERITY_RANK[opts["fail-on"]] ?? SEVERITY_RANK.MAJOR;

  const units = selectUnits(root);

  // Optional LLM pass (opt-in): provision once, review each unit.
  let llm = null;
  if (opts.llm && units.length > 0) {
    llm = await runLlm(units, root);
  }

  const perUnit = [];
  const allFindings = [];

  for (const unit of units) {
    const findings = [];
    for (const mod of staticModules) {
      try {
        findings.push(...mod.review(unit));
      } catch (err) {
        console.error(`⚠ ${mod.metadata.name} failed on ${unit.plugin}/${unit.name}: ${err?.message ?? err}`);
      }
    }
    const llmNotes = [];
    if (llm) {
      const r = llm.results.get(unit);
      if (r) {
        findings.push(...r.findings);
        if (r.inconclusive) llmNotes.push(`eval-review (llm) inconclusive: ${r.reason}`);
      }
    }
    // Blocking = severity >= fail-on AND dimension not in report-only.
    const blocking = findings.filter(
      (f) => SEVERITY_RANK[f.severity] >= failRank && !reportOnly.has(f.dimension),
    );
    perUnit.push({ unit, findings, blocking, llmNotes });
    allFindings.push(...findings);
    for (const f of findings) ghAnnotate(f);
  }

  const blockingTotal = perUnit.reduce((n, u) => n + u.blocking.length, 0);

  if (opts.json) {
    console.log(
      JSON.stringify(
        {
          repoRoot: root,
          unitCount: units.length,
          llm: llm ? { enabled: true, model: llm.model } : { enabled: false },
          counts: severityCounts(allFindings),
          blocking: blockingTotal,
          verdict: verdict(allFindings),
          units: perUnit.map((u) => ({
            unit: `${u.unit.plugin}/${u.unit.name}`,
            skillDoc: u.unit.skillDoc,
            evalFiles: u.unit.evalFiles.map((e) => e.path),
            notes: u.unit.notes,
            llmNotes: u.llmNotes,
            findings: u.findings,
            blocking: u.blocking.length,
            verdict: verdict(u.findings),
          })),
        },
        null,
        2,
      ),
    );
  } else {
    printText(root, units, perUnit, allFindings, reportOnly, blockingTotal, llm);
  }

  process.exitCode = blockingTotal > 0 ? 1 : 0;
}

function printText(root, units, perUnit, allFindings, reportOnly, blockingTotal, llm) {
  const banner = llm ? `eval-review (static + llm:${llm.model})` : "eval-review (static)";
  console.log(`${banner} — ${units.length} unit(s) evaluated (root: ${root})\n`);
  if (units.length === 0) {
    console.log("No skill+eval units selected. (No matching changes, or use --all.)");
    return;
  }
  for (const { unit, findings, llmNotes } of perUnit) {
    const label = `${unit.plugin}/${unit.name}` + (unit.evalFiles.length ? "" : " (no eval)");
    // Only print units that have something to say, unless --all.
    if (findings.length === 0 && unit.notes.length === 0 && llmNotes.length === 0 && !opts.all) continue;
    console.log(formatUnitReport(label, findings));
    for (const note of unit.notes) console.log(`  NOTE:  ${note}`);
    for (const note of llmNotes) console.log(`  NOTE:  ${note}`);
    console.log("");
  }
  const counts = severityCounts(allFindings);
  const roNote = reportOnly.size ? ` (report-only: ${[...reportOnly].join(", ")})` : "";
  console.log(
    `Summary: ${counts.BLOCKER} BLOCKER, ${counts.MAJOR} MAJOR, ${counts.MINOR} MINOR, ${counts.INFO} INFO${roNote}`,
  );
  console.log(`Overall verdict: ${verdict(allFindings)}`);
  console.log(
    blockingTotal > 0
      ? `\n✗ ${blockingTotal} blocking finding(s) at or above ${opts["fail-on"]}. Failing.`
      : `\n✓ No blocking findings at or above ${opts["fail-on"]}.`,
  );
}

main().catch((err) => {
  console.error(`eval-review: fatal error: ${err?.stack ?? err}`);
  process.exitCode = 1;
});
