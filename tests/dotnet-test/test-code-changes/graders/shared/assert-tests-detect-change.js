// Staged only for grading.
// Usage: node assert-tests-detect-change.js --tests <test.csproj> [--tests ...] --restore <dir>=<original-dir> [--restore ...] [--filter <expression>]
// Proves that the workspace's own tests detect the requested production change: the workspace is
// copied without build output or grader files, each --restore directory is replaced with its
// original (pre-change) copy, and every --tests project is built and run against that original code.
//   detected: a test project fails to compile against the original code (its tests use API the
//             change introduced) or its TRX reports at least one failed test.  -> exit 0
//   survived: every test project builds and all executed tests pass.          -> exit 1
//   error:    restore/build failure outside the test project, zero executed tests, or a crash. -> exit 2
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const skipNames = new Set(['bin', 'obj', 'TestResults', '.git', '.grading', '.tcc-grade', '.vs', 'node_modules']);

function parseArgs(argv) {
  const tests = [];
  const restores = [];
  let filter;
  for (let i = 0; i < argv.length; i += 2) {
    const [flag, value] = [argv[i], argv[i + 1]];
    if (!value) throw new Error(`missing value for ${flag}`);
    if (flag === '--tests') tests.push(value);
    else if (flag === '--filter') filter = value;
    else if (flag === '--restore') {
      const [target, original] = value.split('=');
      if (!target || !original) throw new Error(`--restore expects <dir>=<original-dir>, got '${value}'`);
      restores.push({ target, original });
    } else throw new Error(`unknown argument ${flag}`);
  }
  if (!tests.length || !restores.length) throw new Error('need at least one --tests and one --restore');
  return { tests, restores, filter };
}

function copyTree(src, dest) {
  fs.mkdirSync(dest, { recursive: true });
  for (const entry of fs.readdirSync(src, { withFileTypes: true })) {
    if (skipNames.has(entry.name)) continue;
    const from = path.join(src, entry.name);
    const to = path.join(dest, entry.name);
    if (entry.isDirectory()) copyTree(from, to);
    else if (entry.isFile()) fs.copyFileSync(from, to);
  }
}

function run(cwd, args) {
  const env = { ...process.env, DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1', MSBUILDTERMINALLOGGER: 'false' };
  const result = spawnSync('dotnet', args, { cwd, env, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (result.error) throw result.error;
  return result;
}

const tail = r => `${r.stdout || ''}${r.stderr || ''}`.trim().split(/\r?\n/).slice(-20).join('\n');

function trxTotals(dir) {
  const totals = { executed: 0, failed: 0 };
  if (!fs.existsSync(dir)) return null;
  const files = fs.readdirSync(dir).filter(f => f.endsWith('.trx'));
  if (!files.length) return null;
  for (const file of files) {
    const counters = /<Counters\b[^>]*>/.exec(fs.readFileSync(path.join(dir, file), 'utf8'));
    if (!counters) return null;
    for (const key of Object.keys(totals)) {
      const match = new RegExp(`\\b${key}="(\\d+)"`).exec(counters[0]);
      totals[key] += match ? Number(match[1]) : 0;
    }
  }
  return totals;
}

function evaluateProject(root, project, index, filter) {
  const build = run(root, ['build', project, '-v:q', '--nologo', '-nodeReuse:false', '-p:UseSharedCompilation=false']);
  if (build.status !== 0) {
    const output = `${build.stdout}${build.stderr}`;
    const projectDir = `${path.dirname(path.resolve(root, project)).replace(/\\/g, '/')}/`;
    const testCompileErrors = output.split(/\r?\n/).filter(line =>
      /error CS\d+/.test(line) && line.replace(/\\/g, '/').includes(projectDir));
    if (testCompileErrors.length) {
      return { kind: 'detected', detail: `${project} does not compile against the original code:\n${testCompileErrors.slice(0, 5).join('\n')}` };
    }
    return { kind: 'error', detail: `build of ${project} failed outside its test sources\n${tail(build)}` };
  }
  const resultsDir = path.join(root, `.revert-results-${index}`);
  const options = filter ? ['--filter', filter] : [];
  let test = run(root, ['test', project, '--no-build', '--nologo', '--logger', 'trx', '--results-directory', resultsDir, ...options]);
  if (test.status === null || test.signal) return { kind: 'error', detail: `test run crashed (signal ${test.signal})\n${tail(test)}` };
  let totals = trxTotals(resultsDir);
  if (!totals) {
    // Native MTP does not recognize VSTest's --logger; its runner reports
    // failed and succeeded counts even when no TRX extension is installed.
    test = run(root, ['test', project, '--no-build', ...options]);
    if (test.status === null || test.signal) return { kind: 'error', detail: `test run crashed (signal ${test.signal})\n${tail(test)}` };
    const summary = /\btotal:\s*\d+\s*,?\s*failed:\s*(\d+)\s*,?\s*succeeded:\s*(\d+)\s*,?\s*skipped:\s*\d+\b/i.exec(test.stdout);
    if (summary) {
      const failed = Number(summary[1]);
      totals = { failed, executed: failed + Number(summary[2]) };
    }
  }
  if (!totals) return { kind: 'error', detail: `no readable TRX or MTP summary (exit ${test.status})\n${tail(test)}` };
  if (totals.executed === 0) return { kind: 'error', detail: `${project} executed no tests` };
  if (totals.failed > 0) return { kind: 'detected', detail: `${project}: ${totals.failed} of ${totals.executed} tests fail against the original code` };
  if (test.status !== 0) return { kind: 'error', detail: `${project} exited ${test.status} without a failed test result\n${tail(test)}` };
  return { kind: 'survived', detail: `${project}: all ${totals.executed} tests pass against the original code` };
}

function main() {
  const { tests, restores, filter } = parseArgs(process.argv.slice(2));
  const workspace = process.cwd();
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'tcc-revert-'));
  let exitCode;
  try {
    copyTree(workspace, root);
    for (const { target, original } of restores) {
      const source = path.resolve(workspace, original);
      if (!fs.existsSync(source)) throw new Error(`original copy '${original}' not found`);
      fs.rmSync(path.join(root, target), { recursive: true, force: true });
      copyTree(source, path.join(root, target));
    }
    const results = tests.map((project, index) => {
      if (!fs.existsSync(path.join(root, project))) return { kind: 'error', detail: `test project '${project}' not found` };
      return evaluateProject(root, project, index, filter);
    });
    for (const r of results) console.error(`${r.kind}: ${r.detail}`);
    if (results.some(r => r.kind === 'detected')) {
      console.log('the workspace tests detect the change');
      exitCode = 0;
    } else if (results.some(r => r.kind === 'error')) {
      exitCode = 2;
    } else {
      console.error('no test fails against the original code: the change has no test that proves it');
      exitCode = 1;
    }
  } catch (e) {
    console.error(`grader error: ${e.message}`);
    exitCode = 2;
  }
  // Build servers can briefly hold files in the copy; a leftover temp directory must not change the verdict.
  try {
    fs.rmSync(root, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  } catch (e) {
    console.error(`warning: could not remove ${root}: ${e.message}`);
  }
  process.exit(exitCode);
}

main();
