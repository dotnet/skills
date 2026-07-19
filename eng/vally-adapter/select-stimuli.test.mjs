import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";

const selectorPath = fileURLToPath(new URL("./select-stimuli.mjs", import.meta.url));

function withTempDir(action) {
  const root = mkdtempSync(join(tmpdir(), "vally-selector-test-"));
  try {
    action(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test("selects exact source blocks in requested order", () => {
  withTempDir((root) => {
    const sourcePath = join(root, "eval.vally.yaml");
    const outputPath = join(root, "selected.yaml");
    const source = [
      "name: sample",
      "stimuli:",
      "  - name: First",
      "    prompt: one",
      "  - name: Second",
      "    prompt: two",
      "",
    ].join("\n");
    writeFileSync(sourcePath, source);

    const result = spawnSync(process.execPath, [
      selectorPath,
      "--source", sourcePath,
      "--output", outputPath,
      "--names", "Second,First",
    ], { encoding: "utf8" });

    assert.equal(result.status, 0, result.stderr);
    assert.equal(
      readFileSync(outputPath, "utf8"),
      "name: sample\nstimuli:\n  - name: Second\n    prompt: two\n  - name: First\n    prompt: one\n",
    );
  });
});

test("rejects unknown and duplicate stimulus names", () => {
  withTempDir((root) => {
    const sourcePath = join(root, "eval.vally.yaml");
    const outputPath = join(root, "selected.yaml");
    writeFileSync(sourcePath, "name: sample\nstimuli:\n  - name: First\n    prompt: one\n");

    for (const names of ["Missing", "First,First"]) {
      const result = spawnSync(process.execPath, [
        selectorPath,
        "--source", sourcePath,
        "--output", outputPath,
        "--names", names,
      ], { encoding: "utf8" });
      assert.notEqual(result.status, 0);
    }
  });
});
