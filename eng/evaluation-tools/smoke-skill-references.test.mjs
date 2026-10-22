import assert from "node:assert/strict";
import { resolve } from "node:path";
import test from "node:test";
import { verifyReferenceReads } from "./smoke-skill-references.mjs";

const references = [
  { path: resolve("workspace", "msbuild", "references", "extension-points.md"), heading: "# MSBuild extension points" },
  { path: resolve("workspace", "msbuild", "references", "incremental-build.md"), heading: "# Optimizing MSBuild Incremental Builds" },
];

function read(reference, id, success = true, content = reference.heading) {
  return [
    { type: "tool.execution_start", data: {
      toolCallId: id, toolName: "view", arguments: { path: reference.path, view_range: [1, 2] },
    } },
    { type: "tool.execution_complete", data: {
      toolCallId: id, success, result: { content },
    } },
  ];
}

test("requires one successful actual view of every installed reference", () => {
  const result = verifyReferenceReads(references, references.flatMap((reference, index) => read(reference, String(index))));
  assert.equal(result.passed, true);
  assert.ok(result.reads.every((entry) => entry.attempts === 1 && entry.successfulReads === 1));
});

test("file existence or an assistant's success claim cannot replace a read", () => {
  const events = [
    ...read(references[0], "first"),
    { type: "assistant.message", data: { content: "All references were read successfully." } },
    { type: "tool.execution_start", data: {
      toolName: "glob", arguments: { pattern: "*.md" }, toolCallId: "glob",
    } },
  ];
  const result = verifyReferenceReads(references, events);
  assert.equal(result.passed, false);
  assert.equal(result.reads[1].attempts, 0);
  assert.deepEqual(result.unexpectedTools, ["glob"]);
});

test("rejects a missing-path result even if the tool reports success", () => {
  const result = verifyReferenceReads(references, [
    ...read(references[0], "first", true, "Path does not exist"),
    ...read(references[1], "second"),
  ]);
  assert.equal(result.passed, false);
  assert.equal(result.reads[0].successfulReads, 0);
});

test("does not hide failed reads behind successful retries or shell recovery", () => {
  const result = verifyReferenceReads(references, [
    ...read(references[0], "failed", false, "Path does not exist"),
    ...read(references[0], "retried"),
    ...read(references[1], "second"),
    { type: "tool.execution_start", data: { toolName: "powershell", toolCallId: "shell" } },
  ]);
  assert.equal(result.passed, false);
  assert.equal(result.reads[0].attempts, 2);
  assert.deepEqual(result.unexpectedTools, ["powershell"]);
});

test("rejects reads of a different checkout with matching reference text", () => {
  const result = verifyReferenceReads(references, [
    ...read({ ...references[0], path: resolve("other-checkout", "extension-points.md") }, "wrong"),
    ...read(references[1], "second"),
  ]);
  assert.equal(result.passed, false);
  assert.equal(result.unexpectedViews.length, 1);
});
