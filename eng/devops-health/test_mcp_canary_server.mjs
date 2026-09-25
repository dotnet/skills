import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  createCanaryServer,
  handleMcpMessage,
  TOOL_NAME,
  TOOL_RESULT,
} from "./mcp_canary_server.mjs";

test("echoes the client protocol during initialize", () => {
  const response = handleMcpMessage({
    jsonrpc: "2.0",
    id: 1,
    method: "initialize",
    params: {
      protocolVersion: "2025-03-26",
      capabilities: {},
      clientInfo: { name: "test", version: "1" },
    },
  });

  assert.equal(response.status, 200);
  assert.equal(response.body.result.protocolVersion, "2025-03-26");
  assert.equal(response.evidence.method, "initialize");
  assert.equal(response.evidence.protocolVersion, "2025-03-26");
});

test("lists one deterministic read-only tool", () => {
  const response = handleMcpMessage({
    jsonrpc: "2.0",
    id: 2,
    method: "tools/list",
    params: {},
  });

  assert.equal(response.status, 200);
  assert.deepEqual(
    response.body.result.tools.map(tool => tool.name),
    [TOOL_NAME],
  );
  assert.equal(response.body.result.tools[0].annotations.readOnlyHint, true);
  assert.equal(response.body.result.tools[0].annotations.destructiveHint, false);
});

test("returns the fixed marker only for the canary tool", () => {
  const response = handleMcpMessage({
    jsonrpc: "2.0",
    id: 3,
    method: "tools/call",
    params: { name: TOOL_NAME, arguments: {} },
  });

  assert.equal(response.status, 200);
  assert.equal(response.body.result.content[0].text, TOOL_RESULT);
  assert.equal(response.evidence.toolName, TOOL_NAME);
});

test("rejects unknown methods", () => {
  const response = handleMcpMessage({
    jsonrpc: "2.0",
    id: 4,
    method: "resources/list",
    params: {},
  });

  assert.equal(response.body.error.code, -32601);
});

test("serves the complete initialize, list, and call flow", async t => {
  const directory = await mkdtemp(join(tmpdir(), "mcp-canary-"));
  const evidencePath = join(directory, "evidence.jsonl");
  const server = createCanaryServer({ evidencePath });
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
  });
  t.after(async () => {
    await new Promise(resolve => server.close(resolve));
    await rm(directory, { recursive: true, force: true });
  });
  const { port } = server.address();
  const invoke = async message => {
    const response = await fetch(`http://127.0.0.1:${port}/mcp`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(message),
    });
    return response.status === 202 ? null : response.json();
  };

  const initialized = await invoke({
    jsonrpc: "2.0",
    id: 1,
    method: "initialize",
    params: { protocolVersion: "2025-03-26" },
  });
  await invoke({
    jsonrpc: "2.0",
    method: "notifications/initialized",
    params: {},
  });
  const listed = await invoke({
    jsonrpc: "2.0",
    id: 2,
    method: "tools/list",
    params: {},
  });
  const called = await invoke({
    jsonrpc: "2.0",
    id: 3,
    method: "tools/call",
    params: { name: TOOL_NAME, arguments: {} },
  });

  assert.equal(initialized.result.protocolVersion, "2025-03-26");
  assert.equal(listed.result.tools[0].name, TOOL_NAME);
  assert.equal(called.result.content[0].text, TOOL_RESULT);
  const evidence = (await readFile(evidencePath, "utf8"))
    .trim()
    .split("\n")
    .map(line => JSON.parse(line));
  assert.deepEqual(
    evidence.map(item => item.method),
    [
      "initialize",
      "notifications/initialized",
      "tools/list",
      "tools/call",
    ],
  );
});
