import fs from "node:fs";
import path from "node:path";

const [actualRoot, expectedRoot] = process.argv.slice(2);

if (!actualRoot || !expectedRoot) {
  console.error("Usage: node verify-identical.mjs <actual> <expected>");
  process.exit(2);
}

function collect(root, relative = "") {
  const entries = fs.readdirSync(path.join(root, relative), { withFileTypes: true })
    .filter((entry) => !["bin", "obj", ".vs"].includes(entry.name))
    .sort((left, right) => left.name.localeCompare(right.name));
  const files = [];

  for (const entry of entries) {
    const child = path.join(relative, entry.name);
    if (entry.isDirectory()) {
      files.push(...collect(root, child));
    } else if (entry.isFile()) {
      files.push(child);
    } else {
      throw new Error(`Unsupported fixture entry: ${child}`);
    }
  }

  return files;
}

const actualFiles = collect(actualRoot);
const expectedFiles = collect(expectedRoot);

if (JSON.stringify(actualFiles) !== JSON.stringify(expectedFiles)) {
  console.error("File set changed.");
  process.exit(1);
}

for (const relative of actualFiles) {
  const actual = fs.readFileSync(path.join(actualRoot, relative));
  const expected = fs.readFileSync(path.join(expectedRoot, relative));
  if (!actual.equals(expected)) {
    console.error(`File content changed: ${relative}`);
    process.exit(1);
  }
}

console.log(`PASS: ${actualFiles.length} files are unchanged.`);
