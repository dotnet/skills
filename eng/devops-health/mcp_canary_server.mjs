#!/usr/bin/env node

import { appendFileSync } from "node:fs";
import { createServer } from "node:http";

export const TOOL_NAME = "read_canary";
export const TOOL_RESULT = "DEVOPS_HEALTH_MCP_CANARY_OK";

export function handleMcpMessage(message) {
  if (!message || typeof message !== "object" || Array.isArray(message)) {
    return {
      status: 400,
      body: {
        jsonrpc: "2.0",
        id: null,
        error: { code: -32600, message: "Invalid request" },
      },
    };
  }

  const { id, method, params } = message;
  const evidence = {
    method,
    protocolVersion: params?.protocolVersion ?? null,
    toolName: params?.name ?? null,
  };

  if (method === "notifications/initialized") {
    return { status: 202, body: null, evidence };
  }
  if (method === "initialize") {
    return {
      status: 200,
      evidence,
      body: {
        jsonrpc: "2.0",
        id,
        result: {
          protocolVersion: params?.protocolVersion,
          capabilities: { tools: { listChanged: false } },
          serverInfo: {
            name: "devops-health-pin-canary",
            version: "1.0.0",
          },
        },
      },
    };
  }
  if (method === "tools/list") {
    return {
      status: 200,
      evidence,
      body: {
        jsonrpc: "2.0",
        id,
        result: {
          tools: [
            {
              name: TOOL_NAME,
              description:
                "Return the fixed DevOps Health compatibility canary marker.",
              inputSchema: {
                type: "object",
                properties: {},
                additionalProperties: false,
              },
              annotations: {
                readOnlyHint: true,
                destructiveHint: false,
                idempotentHint: true,
                openWorldHint: false,
              },
            },
          ],
        },
      },
    };
  }
  if (method === "tools/call" && params?.name === TOOL_NAME) {
    return {
      status: 200,
      evidence,
      body: {
        jsonrpc: "2.0",
        id,
        result: {
          content: [{ type: "text", text: TOOL_RESULT }],
          isError: false,
        },
      },
    };
  }
  return {
    status: 200,
    evidence,
    body: {
      jsonrpc: "2.0",
      id: id ?? null,
      error: { code: -32601, message: `Unsupported method: ${method}` },
    },
  };
}

function writeJson(response, status, body) {
  if (body === null) {
    response.writeHead(status);
    response.end();
    return;
  }
  response.writeHead(status, { "content-type": "application/json" });
  response.end(JSON.stringify(body));
}

export function createCanaryServer({ evidencePath }) {
  return createServer((request, response) => {
    if (request.method === "GET" && request.url === "/health") {
      writeJson(response, 200, { status: "ok" });
      return;
    }
    if (request.method !== "POST" || request.url !== "/mcp") {
      writeJson(response, 404, {
        jsonrpc: "2.0",
        id: null,
        error: { code: -32601, message: "Not found" },
      });
      return;
    }

    let raw = "";
    request.setEncoding("utf8");
    request.on("data", chunk => {
      raw += chunk;
      if (raw.length > 65536) {
        request.destroy();
      }
    });
    request.on("end", () => {
      let message;
      try {
        message = JSON.parse(raw);
      } catch {
        writeJson(response, 400, {
          jsonrpc: "2.0",
          id: null,
          error: { code: -32700, message: "Parse error" },
        });
        return;
      }

      const result = handleMcpMessage(message);
      if (result.evidence) {
        appendFileSync(evidencePath, `${JSON.stringify(result.evidence)}\n`, {
          encoding: "utf8",
          mode: 0o600,
        });
      }
      writeJson(response, result.status, result.body);
    });
  });
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const port = Number(process.env.CANARY_PORT ?? "18081");
  const evidencePath = process.env.CANARY_EVIDENCE;
  if (!Number.isInteger(port) || port < 1 || port > 65535 || !evidencePath) {
    console.error("CANARY_PORT and CANARY_EVIDENCE are required");
    process.exit(2);
  }
  const server = createCanaryServer({ evidencePath });
  server.listen(port, "0.0.0.0", () => {
    console.log(`MCP canary server listening on ${port}`);
  });
}
