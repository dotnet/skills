#!/usr/bin/env node

import { readdirSync, statSync, writeFileSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";

function listFiles(root) {
  const files = [];
  const visit = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) visit(path);
      else files.push(relative(root, path));
    }
  };
  visit(root);
  return files.sort();
}

export function consolidateSmoke(root, entries) {
  const resolvedRoot = resolve(root);
  const shards = entries.map((entry) => {
    const artifactName = `vally-results-${entry.name}`;
    const path = join(resolvedRoot, artifactName);
    const present = (() => {
      try {
        return statSync(path).isDirectory();
      } catch {
        return false;
      }
    })();
    return {
      artifactName,
      entry,
      present,
      files: present ? listFiles(path) : [],
    };
  });
  return {
    expectedShardCount: shards.length,
    presentShardCount: shards.filter((shard) => shard.present).length,
    missingShards: shards.filter((shard) => !shard.present).map((shard) => shard.artifactName),
    shards,
  };
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const { values } = parseArgs({
    options: {
      root: { type: "string" },
      expected: { type: "string" },
      output: { type: "string" },
    },
    strict: true,
  });
  if (!values.root || !values.expected || !values.output) {
    throw new Error("--root, --expected, and --output are required");
  }
  const entries = JSON.parse(values.expected);
  const inventory = consolidateSmoke(values.root, entries);
  writeFileSync(values.output, `${JSON.stringify(inventory, null, 2)}\n`);
  console.log(
    `Smoke shards: ${inventory.presentShardCount}/${inventory.expectedShardCount}; ` +
    `missing: ${inventory.missingShards.join(", ") || "none"}`,
  );
}
