import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { inventoryTreatment } from "./inventory-treatment.mjs";

test("inventories one skilled result without baseline or comparison evidence", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-treatment-inventory-"));
  try {
    const run = join(root, "2026-08-07T00-00-00Z");
    mkdirSync(join(run, "skilled"), { recursive: true });
    writeFileSync(join(run, "skilled", "results.jsonl"), `${JSON.stringify({
      type: "trial-result",
      variant: "skilled",
      evalName: "perf",
      trajectory: { id: "trajectory-1", endReason: "completed" },
      gradeResult: { passed: true, score: 1, stimulusName: "scenario", trajectoryId: "trajectory-1" },
    })}\n`);
    const manifest = join(root, "treatment-inputs.json");
    writeFileSync(manifest, `${JSON.stringify({ expectedItems: [{ trial: 1, variant: "skilled" }] })}\n`);

    const inventory = inventoryTreatment(root, manifest);
    assert.equal(inventory.expectedCount, 1);
    assert.equal(inventory.actualCount, 1);
    assert.equal(inventory.missingCount, 0);
    assert.equal(inventory.baselineExecuted, false);
    assert.equal(inventory.comparisonEvidencePresent, false);
    assert.equal(inventory.records[0].gradePassed, true);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("preserves explicit missing, baseline, and comparison evidence", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-treatment-inventory-"));
  try {
    const run = join(root, "2026-08-07T00-00-00Z");
    mkdirSync(join(run, "baseline"), { recursive: true });
    mkdirSync(join(run, "_comparison-evidence"), { recursive: true });
    writeFileSync(join(run, "_comparison-evidence", "compare.jsonl"), "{}\n");
    const manifest = join(root, "treatment-inputs.json");
    writeFileSync(manifest, `${JSON.stringify({ expectedItems: [{ trial: 1, variant: "skilled" }] })}\n`);

    const inventory = inventoryTreatment(root, manifest);
    assert.equal(inventory.actualCount, 0);
    assert.equal(inventory.missingCount, 1);
    assert.equal(inventory.baselineExecuted, true);
    assert.equal(inventory.comparisonEvidencePresent, true);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
