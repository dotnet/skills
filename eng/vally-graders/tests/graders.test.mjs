// graders.test.mjs — node:test suite for the eval-review graders.
//
// Mirrors vally's packages/core/tests/grader-plugin.test.ts style: it loads the
// plugin via registerGraders into a stub registry, asserts grader metadata, and
// runs each static grader's review() against on-disk "leaky" and "clean" fixture
// units plus targeted false-positive guards. The llm grader (eval-review) is
// exercised through its pure prompt/mapping helpers and an injected fake client,
// so these tests need no model and no network.

import { test } from "node:test";
import assert from "node:assert/strict";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

import { registerGraders, graders, staticModules, llmModule } from "../index.mjs";
import { allUnits, unitFromEvalDir } from "../lib/locate.mjs";
import * as assertionHardening from "../graders/assertion-hardening.mjs";
import * as negativeScenario from "../graders/negative-scenario.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const FIXTURE_REPO = join(HERE, "fixtures", "repo");

function unitByName(name) {
  const u = allUnits(FIXTURE_REPO).find((x) => x.name === name);
  assert.ok(u, `fixture unit '${name}' should be discovered`);
  return u;
}

function findingsFor(moduleName, unit) {
  const mod = staticModules.find((m) => m.metadata.name === moduleName);
  assert.ok(mod, `module '${moduleName}' should exist`);
  return mod.review(unit);
}

const has = (findings, sev) => findings.some((f) => f.severity === sev);

// ---------------------------------------------------------------------------
// Plugin contract: registerGraders + metadata
// ---------------------------------------------------------------------------

test("registerGraders registers all graders into a stub registry", () => {
  const registered = [];
  registerGraders({ register: (g) => registered.push(g) });
  assert.equal(registered.length, graders.length);
  const names = registered.map((g) => g.metadata.name).sort();
  assert.deepEqual(names, [
    "assertion-hardening",
    "eval-deleading",
    "eval-review",
    "meta-commentary",
    "negative-scenario",
  ]);
});

test("every grader exposes contract-valid metadata", () => {
  for (const g of graders) {
    const m = g.metadata;
    assert.ok(m.name, "name");
    assert.ok(["static", "complex-static", "slm", "llm"].includes(m.determinism), `determinism ${m.determinism}`);
    assert.ok(typeof g.grade === "function", "grade() is a function");
  }
});

test("static graders are free and model-free; eval-review is the only llm grader", () => {
  const llm = graders.filter((g) => g.metadata.determinism === "llm");
  assert.equal(llm.length, 1);
  assert.equal(llm[0].metadata.name, "eval-review");
  for (const m of staticModules) {
    assert.notEqual(m.metadata.determinism, "llm");
    assert.equal(m.metadata.costProfile, "free");
  }
});

// ---------------------------------------------------------------------------
// Leaky fixture: each static grader must catch its dimension
// ---------------------------------------------------------------------------

test("eval-deleading flags a fixture comment that states the fix (MAJOR)", () => {
  const f = findingsFor("eval-deleading", unitByName("leaky-skill"));
  assert.ok(has(f, "MAJOR"), "expected a MAJOR de-leading finding");
  const major = f.find((x) => x.severity === "MAJOR");
  assert.match(major.proof, /the right fix is/i);
  assert.match(major.file, /WidgetProcessor\.cs$/);
});

test("assertion-hardening flags a migrate-existing scenario with no result gate (MAJOR)", () => {
  const f = findingsFor("assertion-hardening", unitByName("leaky-skill"));
  assert.ok(has(f, "MAJOR"), "expected a MAJOR assertion-hardening finding");
  assert.match(f[0].fix, /run_command_and_assert|file_contains/);
});

test("meta-commentary flags a telemetry/provenance aside in SKILL.md (MAJOR)", () => {
  const f = findingsFor("meta-commentary", unitByName("leaky-skill"));
  assert.ok(has(f, "MAJOR"), "expected a MAJOR meta-commentary finding");
  assert.match(f.find((x) => x.severity === "MAJOR").proof, /learned from telemetry/i);
});

test("negative-scenario flags an untested sibling-skill handoff (MAJOR)", () => {
  const f = findingsFor("negative-scenario", unitByName("leaky-skill"));
  assert.ok(has(f, "MAJOR"), "expected a MAJOR negative-scenario finding");
  assert.match(f[0].proof, /DO NOT USE FOR/i);
});

// ---------------------------------------------------------------------------
// Clean fixture: every static grader is silent (false-positive guards)
// ---------------------------------------------------------------------------

test("clean fixture produces no findings from any static grader", () => {
  const unit = unitByName("clean-skill");
  for (const m of staticModules) {
    const f = m.review(unit);
    assert.deepEqual(
      f,
      [],
      `${m.metadata.name} should be silent on the clean fixture, got: ${JSON.stringify(f)}`,
    );
  }
});

test("de-leading does not flag <auto-generated/> or a user's 'is this the right fix?' question", () => {
  // Both live in clean-skill's Thing.cs; covered by the silence test above, but
  // asserted explicitly here as the documented guard.
  const f = findingsFor("eval-deleading", unitByName("clean-skill"));
  assert.deepEqual(f, []);
});

// ---------------------------------------------------------------------------
// Classifier unit tests (intent detection precision/recall)
// ---------------------------------------------------------------------------

test("classify: base-form imperatives are mutating; 3rd-person descriptions are not", () => {
  const { classify } = assertionHardening;
  assert.equal(classify("Write a C# class named Foo"), "mutating");
  assert.equal(classify("Help me migrate my attribute to ExecuteAsync"), "mutating");
  // "generates" describes the project, not an instruction:
  assert.equal(
    classify("Build this project and diagnose why it fails. The project generates a source file."),
    "advisory",
  );
  assert.equal(classify("Diagnose why this fails. Do not modify anything."), "advisory");
});

test("classifyScenario: the scenario name wins over a misleading prompt", () => {
  const { classifyScenario } = assertionHardening;
  // Name says recommend/advise even though the prompt asks to "bump":
  assert.equal(
    classifyScenario({
      name: "Recommend CPM when updating packages",
      prompt: "Can you bump everything to the latest and get them consistent?",
    }),
    "advisory",
  );
  assert.equal(
    classifyScenario({ name: "Migrate custom attribute to ExecuteAsync", prompt: "..." }),
    "mutating",
  );
});

// ---------------------------------------------------------------------------
// Severity-tiering guards
// ---------------------------------------------------------------------------

test("assertion-hardening: produce-new code with no setup files is MINOR, not MAJOR", () => {
  // Synthetic unit: "Write a class" (produce-new), no setup files, output-only.
  const unit = {
    evalFiles: [
      {
        path: "tests/x/y/eval.yaml",
        parsed: {
          format: "native",
          text: 'scenarios:\n  - name: "Write a helper class"\n',
          scenarios: [
            {
              name: "Write a helper class",
              prompt: "Write a C# helper class that formats dates.",
              assertionTypes: ["output_contains"],
              hasExpectActivationFalse: false,
              setupFiles: [],
              copyTestFiles: false,
            },
          ],
        },
      },
    ],
  };
  const f = assertionHardening.review(unit);
  assert.equal(f.length, 1);
  assert.equal(f[0].severity, "MINOR");
});

test("negative-scenario: a generic exclusion (no sibling handoff) is MINOR, not MAJOR", () => {
  const unit = {
    skillDoc: "plugins/x/skills/y/SKILL.md",
    skillDocText: "# y\nDO NOT USE FOR: general chit-chat or unrelated languages.\n",
    evalFiles: [
      { path: "tests/x/y/eval.yaml", parsed: { format: "native", scenarios: [{ hasExpectActivationFalse: false }] } },
    ],
  };
  const f = negativeScenario.review(unit);
  assert.equal(f.length, 1);
  assert.equal(f[0].severity, "MINOR");
});

test("negative-scenario: satisfied when any scenario asserts non-activation", () => {
  const unit = {
    skillDoc: "plugins/x/skills/y/SKILL.md",
    skillDocText: "# y\nDO NOT USE FOR: creating servers (use other-skill instead).\n",
    evalFiles: [
      { path: "tests/x/y/eval.yaml", parsed: { format: "native", scenarios: [{ hasExpectActivationFalse: true }] } },
    ],
  };
  assert.deepEqual(negativeScenario.review(unit), []);
});

// ---------------------------------------------------------------------------
// eval-review LLM grader (no model / no network — pure helpers + fake client)
// ---------------------------------------------------------------------------

const fakeClient = (args) => ({
  async judge() {
    return { args, tokenUsage: { inputTokens: 1, outputTokens: 1, model: "fake" }, latencyMs: 1, remindersUsed: 0 };
  },
});

test("eval-review buildPrompt embeds the skill text, scenarios, and dimension rubric", () => {
  const unit = {
    plugin: "x",
    name: "y",
    kind: "skill",
    skillDoc: "plugins/x/skills/y/SKILL.md",
    skillDocText: "# y\nUse an unusual sentinel phrase QUOKKA-42 here.\n",
    evalFiles: [
      {
        path: "tests/x/y/eval.yaml",
        parsed: {
          format: "native",
          text: "scenarios: []",
          scenarios: [{ name: "Scenario Alpha", prompt: "do the thing", assertionTypes: ["output_contains"] }],
        },
      },
    ],
    fixtures: [],
  };
  const { systemMessage, userMessage } = llmModule.buildPrompt(unit);
  assert.match(systemMessage, /fixture-integrity/);
  assert.match(systemMessage, /design-balance/);
  assert.match(systemMessage, /EVIDENCE DISCIPLINE/);
  assert.match(userMessage, /QUOKKA-42/);
  assert.match(userMessage, /Scenario Alpha/);
  assert.match(userMessage, /tests\/x\/y\/eval\.yaml/);
});

test("eval-review mapFindings enforces evidence discipline and anti-fabrication", () => {
  const unit = {
    plugin: "x",
    name: "y",
    skillDoc: "plugins/x/skills/y/SKILL.md",
    skillDocText: "This guidance was learned from telemetry across many runs.",
    evalFiles: [],
    fixtures: [],
  };
  const args = {
    findings: [
      // Verified quote + known file -> MAJOR stays MAJOR, file kept, rule mapped.
      {
        severity: "MAJOR",
        dimension: "D-design-balance",
        file: "plugins/x/skills/y/SKILL.md",
        proof: "learned from telemetry",
        why: "provenance claim",
        fix: "remove it",
      },
      // Unverifiable quote -> proof dropped -> downgraded to MINOR.
      {
        severity: "MAJOR",
        dimension: "C-fixture-integrity",
        file: "plugins/x/skills/y/SKILL.md",
        proof: "this exact sentence never appears in the source at all",
        why: "made-up",
        fix: "n/a",
      },
      // Unknown/hallucinated file -> location nulled (proof still verifies -> MAJOR).
      {
        severity: "MAJOR",
        dimension: "DC-domain-correctness",
        file: "plugins/x/skills/y/Ghost.cs",
        proof: "learned from telemetry",
        why: "cited a non-existent file",
        fix: "cite a real file",
      },
    ],
  };
  const out = llmModule.mapFindings(args, unit);
  assert.equal(out.length, 3);

  assert.equal(out[0].severity, "MAJOR");
  assert.equal(out[0].dimension, "eval-review");
  assert.equal(out[0].rule, "D");
  assert.equal(out[0].file, "plugins/x/skills/y/SKILL.md");

  assert.equal(out[1].severity, "MINOR", "unverifiable quote must downgrade");
  assert.equal(out[1].proof, null);
  assert.match(out[1].why, /unverified quote omitted/);

  assert.equal(out[2].file, null, "hallucinated file must be dropped");
  assert.equal(out[2].severity, "MAJOR", "verified proof keeps severity even without a file");
  assert.equal(out[2].rule, "DC");
});

test("eval-review reviewUnit maps an injected client's output to findings (no network)", async () => {
  const unit = unitByName("leaky-skill");
  const client = fakeClient({
    findings: [
      { severity: "MINOR", dimension: "C-fixture-integrity", why: "smell", fix: "tighten" },
    ],
    verdict: "Fix-then-ship",
    summary: "ok",
  });
  const r = await llmModule.reviewUnit(unit, { client, model: "test-model" });
  assert.equal(r.ran, true);
  assert.equal(r.inconclusive, false);
  assert.equal(r.findings.length, 1);
  assert.equal(r.findings[0].dimension, "eval-review");
  assert.equal(r.findings[0].rule, "C");
  assert.equal(r.model, "test-model");
});

test("eval-review reviewUnit reports inconclusive (never throws) when the client fails", async () => {
  const unit = unitByName("leaky-skill");
  const boom = {
    async judge() {
      throw new Error("simulated model/timeout failure");
    },
  };
  const r = await llmModule.reviewUnit(unit, { client: boom, model: "test-model" });
  assert.equal(r.inconclusive, true);
  assert.equal(r.ran, false);
  assert.equal(r.findings.length, 0);
  assert.match(r.reason, /model call failed/);
});

test("eval-review grader adapter returns a contract-valid, non-failing result on inconclusive", async () => {
  const boom = {
    async judge() {
      throw new Error("simulated failure");
    },
  };
  const g = llmModule.createGrader({ client: boom, model: "test-model" });
  const result = await g.grade({
    stimulus: { environment: { skills: ["plugins/demo/skills/leaky-skill"] } },
    config: { cwd: FIXTURE_REPO },
  });
  assert.equal(result.name, "eval-review");
  assert.equal(result.passed, true, "inconclusive must not fail the grader");
  assert.equal(result.label, "inconclusive");
});

// ---------------------------------------------------------------------------
// Discovery
// ---------------------------------------------------------------------------

test("locate: discovers both fixture units with their skill docs", () => {
  const units = allUnits(FIXTURE_REPO).map((u) => u.name).sort();
  assert.deepEqual(units, ["clean-skill", "leaky-skill"]);
  const leaky = unitFromEvalDir(FIXTURE_REPO, join(FIXTURE_REPO, "tests", "demo", "leaky-skill"));
  assert.ok(leaky.skillDocText, "resolves SKILL.md");
  assert.equal(leaky.evalFiles.length, 1);
});
