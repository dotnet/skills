import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { consolidateSmoke } from "./consolidate-smoke.mjs";

test("inventories completed shards and preserves explicit missing evidence", () => {
  const root = mkdtempSync(join(tmpdir(), "vally-smoke-consolidate-"));
  try {
    const entries = [{ name: "opus--one" }, { name: "haiku--two" }];
    const completed = join(root, "vally-results-opus--one");
    mkdirSync(join(completed, "nested"), { recursive: true });
    writeFileSync(join(completed, "nested", "results.json"), "{}\n");

    const inventory = consolidateSmoke(root, entries);
    assert.equal(inventory.expectedShardCount, 2);
    assert.equal(inventory.presentShardCount, 1);
    assert.deepEqual(inventory.missingShards, ["vally-results-haiku--two"]);
    assert.deepEqual(inventory.shards[0].files, ["nested/results.json"]);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
