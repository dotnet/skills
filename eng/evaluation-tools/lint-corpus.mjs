import { readdirSync } from 'node:fs';
import { relative, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ansiPattern = /\u001b\[[0-?]*[ -/]*[@-~]/g;

function normalizePath(path) {
  return path.replaceAll('\\', '/').replace(/^\.\//, '');
}

export function discoverEvalSpecs(root, cwd = process.cwd()) {
  const specs = [];

  function visit(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = resolve(directory, entry.name);
      if (entry.isDirectory()) {
        visit(path);
      } else if (entry.isFile() && entry.name === 'eval.yaml') {
        specs.push(normalizePath(relative(cwd, path)));
      }
    }
  }

  visit(resolve(cwd, root));
  return specs.sort();
}

export function reportedEvalSpecs(output) {
  return [...new Set(reportedEvalSpecOccurrences(output))].sort();
}

function reportedEvalSpecOccurrences(output) {
  const specs = [];
  const cleanOutput = output.replace(ansiPattern, '');
  for (const line of cleanOutput.split(/\r?\n/)) {
    const match = /^(?:Eval:\s+|✔\s+)(.+?eval\.yaml)(?:\s+is valid)?\s*$/i.exec(line);
    if (match) specs.push(normalizePath(match[1].trim()));
  }
  return specs;
}

export function validateCorpusLint({
  root = 'tests',
  cwd = process.cwd(),
  vallyScript = fileURLToPath(new URL('./vally.mjs', import.meta.url)),
} = {}) {
  const expected = discoverEvalSpecs(root, cwd);
  const result = spawnSync(
    process.execPath,
    [vallyScript, 'lint', '--eval-spec', root],
    {
      cwd,
      encoding: 'utf8',
      maxBuffer: 16 * 1024 * 1024,
    },
  );

  process.stdout.write(result.stdout ?? '');
  process.stderr.write(result.stderr ?? '');

  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`Vally corpus lint failed with exit code ${result.status}.`);
  }

  const output = `${result.stdout ?? ''}\n${result.stderr ?? ''}`;
  const occurrences = reportedEvalSpecOccurrences(output);
  const reported = [...new Set(occurrences)].sort();
  const expectedSet = new Set(expected);
  const reportedSet = new Set(reported);
  const missing = expected.filter(path => !reportedSet.has(path));
  const unexpected = reported.filter(path => !expectedSet.has(path));
  const counts = new Map();
  for (const path of occurrences) counts.set(path, (counts.get(path) ?? 0) + 1);
  const duplicates = [...counts]
    .filter(([, count]) => count > 1)
    .map(([path, count]) => `${path} (${count})`);

  if (missing.length > 0 || unexpected.length > 0 || duplicates.length > 0) {
    const details = [
      missing.length > 0 ? `Missing (${missing.length}): ${missing.join(', ')}` : null,
      unexpected.length > 0 ? `Unexpected (${unexpected.length}): ${unexpected.join(', ')}` : null,
      duplicates.length > 0 ? `Duplicate reports (${duplicates.length}): ${duplicates.join(', ')}` : null,
    ].filter(Boolean).join('\n');
    throw new Error(
      `Vally reported ${reported.length}/${expected.length} discovered eval specs.\n${details}`,
    );
  }

  console.log(`Validated ${reported.length}/${expected.length} eval specs with Vally.`);
  return { expected, reported };
}

const invokedPath = process.argv[1] ? resolve(process.argv[1]) : null;
if (invokedPath === fileURLToPath(import.meta.url)) {
  try {
    validateCorpusLint({ root: process.argv[2] ?? 'tests' });
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  }
}
