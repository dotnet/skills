// Staged only for grading. Usage: node assert-tests-kill-mutants.js <mutants-dir>
// Proves the agent's own tests detect removal of the requested feature. Each subdirectory of
// <mutants-dir> is one mutant whose files overlay the workspace by relative path. For every
// mutant, the workspace is copied without build output, harness files, or grader-owned
// GraderChecks, the mutant is applied, and the agent's tests must FAIL. The unmutated copy must
// pass first, so always-failing tests cannot fake detection.
// MUTANT_TEST_RUNNER (a node script) replaces `dotnet test` in the graders' own regression tests.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const mutantsDir = process.argv[2];
if (!mutantsDir || !fs.existsSync(mutantsDir)) {
  console.error(`mutants directory '${mutantsDir}' not found`);
  process.exit(2);
}
const mutants = fs.readdirSync(mutantsDir, { withFileTypes: true })
  .filter(e => e.isDirectory()).map(e => e.name).sort();
if (!mutants.length) {
  console.error(`no mutants under '${mutantsDir}'`);
  process.exit(2);
}

const skipNames = new Set(['bin', 'obj', 'TestResults', '.git', '.grading']);
const skipPaths = new Set(['tests/Billing.Tests/GraderChecks']);
function copyWorkspace(src, dest, rel = '') {
  fs.mkdirSync(dest, { recursive: true });
  for (const e of fs.readdirSync(src, { withFileTypes: true })) {
    const r = rel ? `${rel}/${e.name}` : e.name;
    if (skipNames.has(e.name) || skipPaths.has(r)) continue;
    const from = path.join(src, e.name), to = path.join(dest, e.name);
    if (e.isDirectory()) copyWorkspace(from, to, r);
    else if (e.isFile()) fs.copyFileSync(from, to);
  }
}

function runTests(cwd) {
  const runner = process.env.MUTANT_TEST_RUNNER;
  const r = runner
    ? spawnSync(process.execPath, [runner], { cwd, encoding: 'utf8' })
    : spawnSync('dotnet', ['test', 'Fixture.sln', '-v:q', '--nologo'], { cwd, encoding: 'utf8' });
  if (r.error) throw r.error;
  return r.status;
}

function withCopy(mutant, fn) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bc-mutant-'));
  try {
    copyWorkspace(process.cwd(), root);
    if (mutant) copyWorkspace(path.join(mutantsDir, mutant), root);
    return fn(root);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

if (withCopy(null, runTests) !== 0) {
  console.error('agent tests do not pass on the unmutated workspace');
  process.exit(1);
}
const survivors = mutants.filter(m => withCopy(m, runTests) === 0);
if (survivors.length) {
  console.error(`agent tests did not detect removal of the feature; surviving mutants: ${survivors.join(', ')}`);
  process.exit(1);
}
console.log(`agent tests killed every mutant: ${mutants.join(', ')}`);
