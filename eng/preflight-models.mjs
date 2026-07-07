#!/usr/bin/env node

/**
 * preflight-models.mjs — verify a set of model ids is available to the current
 * Copilot token, using the SAME path the vally eval uses: the GitHub Copilot
 * SDK's `CopilotClient.listModels()` (JSON-RPC to the bundled Copilot CLI).
 *
 * vally's CLI has no `models` subcommand, so this small script is the faithful
 * up-front check for the vally workflow — the node analogue of the .NET
 * `skill-validator list-models` preflight used by the gating workflow.
 *
 *   node eng/preflight-models.mjs --require '["claude-opus-4.8","gpt-5.5"]'
 *   node eng/preflight-models.mjs --require "claude-opus-4.8,gpt-5.5"
 *   node eng/preflight-models.mjs            # just print available ids
 *
 * Auth: reads the PAT from GITHUB_TOKEN, then COPILOT_GITHUB_TOKEN, then
 * COPILOT_SDK_AUTH_TOKEN. Exits non-zero if any required id is missing, or if
 * the model list cannot be enumerated at all (a preflight must be conclusive).
 */

import { resolve as resolvePath } from "node:path";
import { fileURLToPath } from "node:url";

function parseArgs(argv) {
  const out = { require: null, json: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--require") out.require = argv[++i];
    else if (a === "--json") out.json = true;
  }
  return out;
}

/** Tolerate a JSON array, or a comma/space-separated list. De-dupe, keep order. */
export function parseRequired(value) {
  if (!value || !value.trim()) return [];
  let s = value.trim();
  if (s.startsWith("[") && s.endsWith("]")) {
    try {
      const arr = JSON.parse(s);
      if (Array.isArray(arr)) return [...new Set(arr.map((x) => String(x).trim()).filter(Boolean))];
    } catch {
      // fall through to delimiter split (strip brackets first)
      s = s.slice(1, -1);
    }
  }
  return [
    ...new Set(
      s
        .split(/[\s,]+/)
        .map((x) => x.trim().replace(/^["']|["']$/g, ""))
        .filter(Boolean),
    ),
  ];
}

async function main() {
  const { require: requireArg, json } = parseArgs(process.argv.slice(2));

  const token =
    process.env.GITHUB_TOKEN ||
    process.env.COPILOT_GITHUB_TOKEN ||
    process.env.COPILOT_SDK_AUTH_TOKEN ||
    "";

  let CopilotClient;
  try {
    ({ CopilotClient } = await import("@github/copilot-sdk"));
  } catch (e) {
    console.error(
      "::error::Cannot load @github/copilot-sdk. Install it (it ships as a dependency of " +
        "@microsoft/vally-cli) before running the preflight.",
    );
    console.error(String(e && e.message ? e.message : e));
    process.exit(1);
  }

  const client = new CopilotClient(token ? { gitHubToken: token } : {});
  let ids;
  try {
    await client.start();
    const models = await client.listModels();
    ids = models.map((m) => m.id);
  } catch (e) {
    console.error("::error::Failed to enumerate available models via the Copilot SDK.");
    console.error(String(e && e.stack ? e.stack : e));
    try {
      await client.stop();
    } catch {
      /* ignore */
    }
    process.exit(1);
  }
  try {
    await client.stop();
  } catch {
    /* ignore */
  }

  const sorted = [...ids].sort();
  const required = parseRequired(requireArg);

  // Privacy: never dump the full available model roster into CI logs. The CI
  // preflight always passes --require, so in that path we only confirm the
  // (already-public, repo-configured) required ids and, on failure, name just the
  // MISSING ones plus an available count. The full list is emitted only on explicit
  // local invocations: --json, or a bare run with no --require.
  if (required.length === 0) {
    if (json) {
      console.log(JSON.stringify(sorted));
    } else {
      console.log(`Available models (${sorted.length}):`);
      for (const id of sorted) console.log(`  ${id}`);
    }
    process.exit(0);
  }

  const available = new Set(ids);
  const missing = required.filter((r) => !available.has(r));
  if (missing.length > 0) {
    console.error(
      `::error::Required model id(s) unavailable to this Copilot token: ${missing.join(", ")}`,
    );
    console.error(`Missing model(s): ${missing.join(", ")}`);
    console.error(`Available model count: ${sorted.length} (list withheld).`);
    process.exit(1);
  }

  console.log(`All ${required.length} required model(s) available: ${required.join(", ")}`);
  process.exit(0);
}

// Only spawn the client when run directly; importing (e.g. from tests) is inert.
if (resolvePath(fileURLToPath(import.meta.url)) === resolvePath(process.argv[1] || "")) {
  main().catch((e) => {
    console.error("::error::preflight-models failed unexpectedly.");
    console.error(String(e && e.stack ? e.stack : e));
    process.exit(1);
  });
}