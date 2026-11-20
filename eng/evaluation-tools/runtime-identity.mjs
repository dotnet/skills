#!/usr/bin/env node
// Record the identity of the evaluation toolchain before trials run: the
// pinned package versions, the Copilot runtime version that actually starts
// (the runtime can differ from the pinned @github/copilot package), and a hash
// of the exact tool schema each model is offered. The Vally adapter stamps this
// manifest on every verdict so model-specific behavior can be attributed to the
// model, the runtime, or the tool surface instead of being guessed afterwards.
//
// Usage:
//   node runtime-identity.mjs --output <file> --model <id> [--model <id> ...]
//
// Failures to start the runtime are recorded in the manifest's `errors` array
// rather than aborting: provenance is diagnostic and must never block an
// evaluation. The process exits non-zero only when the manifest cannot be written.

import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

export const RUNTIME_IDENTITY_SCHEMA_VERSION = 1;
const STEP_TIMEOUT_MS = 60_000;

function canonicalize(value) {
  if (Array.isArray(value)) return value.map(canonicalize);
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.keys(value)
        .sort()
        .filter((key) => value[key] !== undefined)
        .map((key) => [key, canonicalize(value[key])]),
    );
  }
  return value;
}

/**
 * Identity of the tool surface offered to a model: a SHA-256 over every
 * model-visible field of every tool, independent of list and key order.
 */
export function toolSchemaIdentity(tools) {
  const canonical = (tools ?? [])
    .map((tool) => canonicalize({
      name: tool.name,
      namespacedName: tool.namespacedName ?? null,
      description: tool.description ?? null,
      parameters: tool.parameters ?? null,
      instructions: tool.instructions ?? null,
    }))
    .sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
  return {
    sha256: createHash('sha256').update(JSON.stringify(canonical)).digest('hex'),
    toolCount: canonical.length,
    toolNames: canonical.map((tool) => tool.name),
  };
}

function packageVersion(relativePackageJson) {
  try {
    const url = new URL(relativePackageJson, import.meta.url);
    return JSON.parse(readFileSync(url, 'utf8')).version ?? null;
  } catch {
    return null;
  }
}

function withTimeout(promise, label) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error(`${label} timed out after ${STEP_TIMEOUT_MS} ms`)), STEP_TIMEOUT_MS);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

export async function collectRuntimeIdentity(models) {
  const manifest = {
    schemaVersion: RUNTIME_IDENTITY_SCHEMA_VERSION,
    generatedAt: new Date().toISOString(),
    platform: { os: process.platform, arch: process.arch, node: process.version },
    toolchain: {
      vallyVersion: packageVersion('./node_modules/@microsoft/vally-cli/package.json'),
      sdkVersion: packageVersion('./node_modules/@github/copilot-sdk/package.json'),
      copilotPackageVersion: packageVersion('./node_modules/@github/copilot/package.json'),
      runtimeVersion: null,
      protocolVersion: null,
    },
    toolSchemas: {},
    errors: [],
  };

  let client;
  try {
    // Install the shared SDK startup guard before the SDK creates a client.
    await import('./sdk-startup.mjs');
    const { CopilotClient } = await import('@github/copilot-sdk');
    client = new CopilotClient({ logLevel: 'error' });
    await withTimeout(client.start(), 'runtime start');
    const status = await withTimeout(client.getStatus(), 'runtime status');
    manifest.toolchain.runtimeVersion = status?.version ?? null;
    manifest.toolchain.protocolVersion = status?.protocolVersion ?? null;
    for (const model of models) {
      try {
        const { tools } = await withTimeout(client.rpc.tools.list({ model }), `tools.list(${model})`);
        manifest.toolSchemas[model] = toolSchemaIdentity(tools);
      } catch (error) {
        manifest.errors.push({ step: 'tools.list', model, message: String(error?.message ?? error) });
      }
    }
  } catch (error) {
    manifest.errors.push({ step: 'runtime', message: String(error?.message ?? error) });
  } finally {
    if (client) {
      await withTimeout(client.stop(), 'runtime stop').catch(() => client.forceStop?.());
    }
  }
  return manifest;
}

async function main() {
  const { values } = parseArgs({
    options: {
      output: { type: 'string' },
      model: { type: 'string', multiple: true },
    },
  });
  if (!values.output) {
    console.error('Usage: runtime-identity.mjs --output <file> --model <id> [--model <id> ...]');
    process.exit(2);
  }
  const models = [...new Set((values.model ?? []).filter(Boolean))];
  const manifest = await collectRuntimeIdentity(models);
  const output = resolve(values.output);
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, `${JSON.stringify(manifest, null, 2)}\n`);
  const schemas = Object.entries(manifest.toolSchemas)
    .map(([model, schema]) => `${model}=${schema.toolCount} tools/${schema.sha256.slice(0, 12)}`)
    .join(', ');
  console.log(
    `Runtime identity: runtime ${manifest.toolchain.runtimeVersion ?? 'unknown'}, ` +
      `SDK ${manifest.toolchain.sdkVersion ?? 'unknown'}, Vally ${manifest.toolchain.vallyVersion ?? 'unknown'}; ` +
      `tool schemas: ${schemas || 'none'}`,
  );
  for (const error of manifest.errors) {
    console.warn(`::warning::runtime identity ${error.step}${error.model ? ` (${error.model})` : ''}: ${error.message}`);
  }
  // A CLI child process can keep the event loop alive after stop().
  process.exit(0);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main();
}
