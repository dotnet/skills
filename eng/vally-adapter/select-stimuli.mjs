#!/usr/bin/env node

import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { parseArgs } from "node:util";

const { values: opts } = parseArgs({
  options: {
    source: { type: "string" },
    output: { type: "string" },
    names: { type: "string" },
    metadata: { type: "string" },
  },
  strict: true,
});

if (!opts.source || !opts.output || !opts.names) {
  console.error("Usage: select-stimuli.mjs --source <eval.vally.yaml> --output <path> --names <comma-separated> [--metadata <path>]");
  process.exit(1);
}

const requestedNames = opts.names.split(",").map((name) => name.trim()).filter(Boolean);
if (requestedNames.length === 0 || new Set(requestedNames).size !== requestedNames.length) {
  throw new Error("Stimulus names must be non-empty and unique");
}

const source = readFileSync(opts.source, "utf8");
const starts = [...source.matchAll(/^  - name: (.+)$/gm)];
if (starts.length === 0) {
  throw new Error(`No stimuli found in ${opts.source}`);
}

const header = source.slice(0, starts[0].index);
const blocks = new Map();
for (let index = 0; index < starts.length; index++) {
  const name = starts[index][1];
  if (blocks.has(name)) {
    throw new Error(`Duplicate stimulus in source: ${name}`);
  }
  const end = index + 1 < starts.length ? starts[index + 1].index : source.length;
  blocks.set(name, source.slice(starts[index].index, end));
}

const missing = requestedNames.filter((name) => !blocks.has(name));
if (missing.length > 0) {
  throw new Error(`Unknown stimulus name(s): ${missing.join(", ")}`);
}

const selected = header + requestedNames.map((name) => blocks.get(name)).join("");
writeFileSync(opts.output, selected);

const sha256 = (content) => createHash("sha256").update(content).digest("hex");
const metadata = {
  source: opts.source,
  output: opts.output,
  selectedStimuli: requestedNames,
  sourceEvalSha256: sha256(source),
  selectedEvalSha256: sha256(selected),
};
if (opts.metadata) {
  writeFileSync(opts.metadata, `${JSON.stringify(metadata, null, 2)}\n`);
}
console.log(JSON.stringify(metadata));
