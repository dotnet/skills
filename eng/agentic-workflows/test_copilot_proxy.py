#!/usr/bin/env python3
"""Exercise the custom-tool contract in the shipping Copilot proxy image."""

from __future__ import annotations

import argparse
import json
import subprocess
from pathlib import Path


PROBE = r"""
const assert = require("node:assert/strict");
const { createCopilotAdapter } = require("/app/providers/copilot");
const { createBodyHandler } = require("/app/body-handler");
const { buildRequestHeaders } = require("/app/request-headers");
const { transformCodexCompatibleResponseBody } = require("/app/codex-compat");

async function main() {
  const adapter = createCopilotAdapter({ COPILOT_GITHUB_TOKEN: "contract-test" });
  const handler = createBodyHandler({
    handleRequestError(error) { throw error; },
    otel: {},
  });
  const patch = "*** Begin Patch\n*** Add File: smoke.txt\n+OK\n*** End Patch\n";
  const customTool = { type: "custom", name: "apply_patch" };
  const functionTool = {
    type: "function", name: "read_file",
    parameters: { type: "object", properties: {} },
  };
  for (const [name, tools, input] of [
    ["no-tools", [], []],
    ["function-tools", [functionTool], []],
    ["custom-tool", [customTool, functionTool], []],
    ["custom-tool-result", [customTool], [
      { type: "custom_tool_call", name: "apply_patch", id: "ctc_call_contract",
        call_id: "patch-1", input: patch },
      { type: "custom_tool_call_output", call_id: "patch-1", output: "Created smoke.txt" },
    ]],
  ]) {
    const body = Buffer.from(JSON.stringify({
      model: "gpt-5.6-sol", tools, input, stream: true,
    }));
    const req = {
      method: "POST", url: "/responses",
      headers: { "content-length": String(body.length) },
    };
    const result = await handler.transformRequestBody(
      body, "copilot", req, "contract-test", adapter.getBodyTransform()
    );
    assert.ok(Buffer.isBuffer(result.body), `${name}: body must remain a Buffer`);
    const headers = buildRequestHeaders(result.body, body.length, req, {
      injectHeaders: {}, provider: "copilot",
      targetHost: "api.githubcopilot.com", requestId: "contract-test",
    });
    assert.equal(headers["content-length"], String(result.body.length));
    assert.equal(headers["transfer-encoding"], undefined);
    const parsed = JSON.parse(result.body);
    assert.equal(parsed.model, "gpt-5.6-sol");
    assert.equal(parsed.stream_options, undefined);
    assert.ok(parsed.tools.every(tool => tool.type === "function"));
    if (tools.includes(customTool)) {
      assert.ok(result.codexCompatibility.customTools.has("apply_patch"));
      const response = Buffer.from(JSON.stringify({ output: [{
        type: "function_call", name: "apply_patch", call_id: "patch-1",
        arguments: JSON.stringify({ patch }),
      }] }));
      const restored = JSON.parse(transformCodexCompatibleResponseBody(
        response, result.codexCompatibility, "copilot"
      ));
      assert.equal(restored.output[0].type, "custom_tool_call");
      assert.equal(restored.output[0].input, patch);
    } else {
      assert.equal(result.codexCompatibility, null);
    }
    if (input.length) {
      assert.equal(parsed.input[0].type, "function_call");
      assert.equal(parsed.input[0].id, "fc_call_contract");
      assert.equal(JSON.parse(parsed.input[0].arguments).patch, patch);
      assert.equal(parsed.input[1].type, "function_call_output");
      assert.equal(parsed.input[1].output, "Created smoke.txt");
    }
    console.log(`PASS ${name}`);
  }
  await assert.rejects(
    handler.transformRequestBody(
      Buffer.from(JSON.stringify({ tools: [{ type: "custom", name: "unsupported" }] })),
      "copilot", { method: "POST", url: "/responses" }, "contract-test",
      adapter.getBodyTransform()
    ),
    error => error.code === "unsupported_custom_tool" && error.statusCode === 400
  );
  console.log("PASS unsupported-custom-tool");
}
main().catch(error => { console.error(error); process.exitCode = 1; });
"""


def proxy_image() -> str:
    workflows = Path(__file__).resolve().parents[2] / ".github" / "workflows"
    images = set()
    for lock in workflows.glob("*.lock.yml"):
        manifest = json.loads(
            lock.read_text(encoding="utf-8")
            .splitlines()[1]
            .removeprefix("# gh-aw-manifest: ")
        )
        images.update(
            container["pinned_image"]
            for container in manifest["containers"]
            if container["image"].startswith("ghcr.io/github/gh-aw-firewall/api-proxy:")
        )
    if len(images) != 1:
        raise RuntimeError(f"Expected one shared pinned Copilot proxy image, got {images}")
    return images.pop()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", help="Override the proxy image for regression reproduction")
    args = parser.parse_args()
    image = args.image or proxy_image()
    print(f"Testing {image}", flush=True)
    return subprocess.run(
        ["docker", "run", "--rm", "--network", "none", "--entrypoint", "node",
         image, "-e", PROBE],
        check=False,
    ).returncode


if __name__ == "__main__":
    raise SystemExit(main())
