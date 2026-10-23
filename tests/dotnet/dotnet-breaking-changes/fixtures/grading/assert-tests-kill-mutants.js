// Staged only for grading. Usage: node assert-tests-kill-mutants.js <mutants-dir>
// Proves the agent's own tests detect removal of the requested feature. Each <name>.json in
// <mutants-dir> is one mutant: a list of C# member declarations (regex over comment/string-masked
// src/**/*.cs) whose full declarations - expression or block bodied, every #if branch - are
// replaced. Only the targeted members change, so agent refactors elsewhere (moved types, new
// helpers, new public API) still compile under the mutant.
// For the unmutated copy and each mutant, the workspace is copied without build output, harness
// files, or grader-owned GraderChecks, then built and tested with a TRX logger.
//   baseline: build must succeed and TRX must show executed tests, none failed.
//   mutant:   killed ONLY if it builds and TRX reports at least one failed test.
// Build/restore failures, crashes, signals, missing TRX, or a failing exit code without a failed
// test result are grader errors (exit 2), never kills. Survivors exit 1.
// MUTANT_TEST_RUNNER (a node script called as `<runner> build` / `<runner> test <results-dir>`)
// replaces dotnet in the graders' own regression tests.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

// Blank out comments and string/char literals (newlines kept) so offsets match the source.
function mask(src) {
  const out = src.split('');
  const blank = (a, b) => { for (let k = a; k < b; k++) if (out[k] !== '\n' && out[k] !== '\r') out[k] = ' '; };
  let i = 0;
  const n = src.length;
  const endOfRegular = (j, interpolated) => {
    let depth = 0;
    while (j < n) {
      const c = src[j];
      if (depth === 0) {
        if (c === '\\') { j += 2; continue; }
        if (c === '"') return j + 1;
        if (interpolated && c === '{') { if (src[j + 1] === '{') { j += 2; continue; } depth++; }
      } else if (c === '"') { j = endOfRegular(j + 1, false); continue; }
      else if (c === '{') depth++;
      else if (c === '}') depth--;
      j++;
    }
    return n;
  };
  while (i < n) {
    const c = src[i];
    if (c === '/' && src[i + 1] === '/') { const e = src.indexOf('\n', i); const end = e < 0 ? n : e; blank(i, end); i = end; continue; }
    if (c === '/' && src[i + 1] === '*') { const e = src.indexOf('*/', i + 2); const end = e < 0 ? n : e + 2; blank(i, end); i = end; continue; }
    if (c === "'") { let j = i + 1; while (j < n && src[j] !== "'") j += src[j] === '\\' ? 2 : 1; blank(i, j + 1); i = j + 1; continue; }
    const m = /^(\$*@?\$*)"/.exec(src.slice(i, i + 8));
    if (m && (c === '"' || c === '$' || c === '@')) {
      const start = i;
      const prefix = m[1];
      let j = i + m[0].length - 1;
      const quotes = /^"+/.exec(src.slice(j))[0].length;
      if (quotes >= 3) {
        const close = src.indexOf('"'.repeat(quotes), j + quotes);
        j = close < 0 ? n : close + quotes;
      } else if (prefix.includes('@')) {
        j++;
        while (j < n) { if (src[j] === '"') { if (src[j + 1] === '"') { j += 2; continue; } j++; break; } j++; }
      } else {
        j = endOfRegular(j + 1, prefix.includes('$'));
      }
      blank(start, j);
      i = j;
      continue;
    }
    i++;
  }
  return out.join('');
}

// End offset (exclusive) of the member body that follows offset `from`, or -1. Preprocessor
// aware: a body whose terminator falls inside #if/#elif/#else branches (for example `=>` then
// `#if NET8_0` / `"net8";` / `#else` / `"net10";` / `#endif`, or a `{ }` per branch) extends to the
// end of the matching #endif line, and later branches are skipped rather than re-parsed.
function bodyEnd(masked, from) {
  let pp = 0, mode = null, depth = 0, terminated = false;
  for (let i = from; i < masked.length; i++) {
    const c = masked[i];
    if (c === '#' && /^[ \t]*$/.test(masked.slice(masked.lastIndexOf('\n', i - 1) + 1, i))) {
      const lineEnd = masked.indexOf('\n', i) < 0 ? masked.length : masked.indexOf('\n', i);
      const word = /^#\s*(\w+)/.exec(masked.slice(i, lineEnd));
      if (word && word[1] === 'if') pp++;
      else if (word && word[1] === 'endif') {
        if (pp === 0) return -1;
        if (--pp === 0 && terminated) return lineEnd;
      }
      i = lineEnd;
      continue;
    }
    if (terminated) continue;
    if (mode === null) {
      if (/\s/.test(c)) continue;
      if (masked.startsWith('=>', i)) { mode = 'expr'; i++; continue; }
      if (c === '{') { mode = 'block'; depth = 1; continue; }
      return -1;
    }
    let done = false;
    if (mode === 'expr') {
      if ('([{'.includes(c)) depth++;
      else if (')]}'.includes(c)) depth--;
      else if (c === ';' && depth === 0) done = true;
    } else if (c === '{') depth++;
    else if (c === '}' && --depth === 0) done = true;
    if (done) {
      if (pp === 0) return i + 1;
      terminated = true;
    }
  }
  return -1;
}

// Replace every declaration matching `declaration` in `src`; returns { text, count }.
function replaceDeclarations(src, declaration, replacement) {
  const masked = mask(src);
  const re = new RegExp(declaration, 'g');
  const spans = [];
  for (let m; (m = re.exec(masked));) {
    const lineStart = masked.lastIndexOf('\n', m.index) + 1;
    const indent = /^[ \t]*/.exec(masked.slice(lineStart))[0].length;
    const end = bodyEnd(masked, m.index + m[0].length);
    if (end < 0) throw new Error(`could not find the body of '${m[0]}'`);
    spans.push([lineStart + indent, end]);
  }
  let text = src;
  for (const [a, b] of spans.reverse()) text = text.slice(0, a) + replacement + text.slice(b);
  return { text, count: spans.length };
}

function csFiles(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    if (['bin', 'obj'].includes(e.name)) return [];
    const p = path.join(dir, e.name);
    return e.isDirectory() ? csFiles(p) : e.name.endsWith('.cs') ? [p] : [];
  });
}

// Apply a mutant spec to the workspace at `root`; throws when a declaration is not found.
function applyMutant(root, spec) {
  const files = csFiles(path.join(root, 'src'));
  for (const { declaration, with: replacement } of spec.replace) {
    let total = 0;
    for (const f of files) {
      const src = fs.readFileSync(f, 'utf8');
      const { text, count } = replaceDeclarations(src, declaration, replacement);
      if (count) { fs.writeFileSync(f, text); total += count; }
    }
    if (!total) throw new Error(`no declaration matching /${declaration}/ under src/`);
  }
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

function run(cwd, phase, resultsDir) {
  const runner = process.env.MUTANT_TEST_RUNNER;
  const args = runner
    ? [process.execPath, [runner, phase, resultsDir].filter(Boolean)]
    : phase === 'build'
      ? ['dotnet', ['build', 'Fixture.sln', '-v:q', '--nologo']]
      : ['dotnet', ['test', 'Fixture.sln', '--no-build', '-v:q', '--nologo', '--logger', 'trx', '--results-directory', resultsDir]];
  const r = spawnSync(args[0], args[1], { cwd, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.error) throw r.error;
  return r;
}

function trxFiles(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    const p = path.join(dir, e.name);
    return e.isDirectory() ? trxFiles(p) : e.name.endsWith('.trx') ? [p] : [];
  });
}

const tail = r => `${r.stdout || ''}${r.stderr || ''}`.trim().split(/\r?\n/).slice(-15).join('\n');

// Returns { kind: 'passed' | 'failed' | 'error', detail }.
function evaluate(root) {
  const build = run(root, 'build');
  if (build.status !== 0) return { kind: 'error', detail: `build/restore failed (status ${build.status}, signal ${build.signal})\n${tail(build)}` };
  const resultsDir = path.join(root, '.mutant-results');
  const t = run(root, 'test', resultsDir);
  if (t.status === null || t.signal) return { kind: 'error', detail: `test run crashed (signal ${t.signal})\n${tail(t)}` };
  const trx = trxFiles(resultsDir);
  if (!trx.length) return { kind: 'error', detail: `no TRX results (status ${t.status})\n${tail(t)}` };
  const totals = { executed: 0, failed: 0 };
  for (const f of trx) {
    const counters = /<Counters\b[^>]*>/.exec(fs.readFileSync(f, 'utf8'));
    if (!counters) return { kind: 'error', detail: `TRX without <Counters>: ${path.basename(f)}` };
    for (const k of Object.keys(totals)) {
      const v = new RegExp(`\\b${k}="(\\d+)"`).exec(counters[0]);
      totals[k] += v ? Number(v[1]) : 0;
    }
  }
  if (totals.executed === 0) return { kind: 'error', detail: 'TRX reports no executed tests' };
  if (totals.failed > 0) {
    return t.status !== 0
      ? { kind: 'failed', detail: `${totals.failed} failed of ${totals.executed} executed` }
      : { kind: 'error', detail: `TRX reports ${totals.failed} failed tests but the run exited 0` };
  }
  if (t.status !== 0) return { kind: 'error', detail: `test run exited ${t.status} without a failed test result\n${tail(t)}` };
  return { kind: 'passed', detail: `${totals.executed} executed, none failed` };
}

function withCopy(spec, fn) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bc-mutant-'));
  try {
    copyWorkspace(process.cwd(), root);
    if (spec) applyMutant(root, spec);
    return fn(root);
  } catch (e) {
    return { kind: 'error', detail: e.message };
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

function main() {
  const mutantsDir = process.argv[2];
  if (!mutantsDir || !fs.existsSync(mutantsDir)) {
    console.error(`mutants directory '${mutantsDir}' not found`);
    process.exit(2);
  }
  const mutants = fs.readdirSync(mutantsDir).filter(f => f.endsWith('.json')).sort();
  if (!mutants.length) {
    console.error(`no mutants under '${mutantsDir}'`);
    process.exit(2);
  }
  const baseline = withCopy(null, evaluate);
  if (baseline.kind !== 'passed') {
    console.error(`unmutated workspace: ${baseline.kind === 'failed' ? 'agent tests fail' : 'grader error'}: ${baseline.detail}`);
    process.exit(baseline.kind === 'failed' ? 1 : 2);
  }
  const errors = [], survivors = [];
  for (const m of mutants) {
    const spec = JSON.parse(fs.readFileSync(path.join(mutantsDir, m), 'utf8'));
    const r = withCopy(spec, evaluate);
    const name = path.basename(m, '.json');
    if (r.kind === 'error') errors.push(`${name}: ${r.detail}`);
    else if (r.kind === 'passed') survivors.push(name);
    else console.log(`killed ${name}: ${r.detail}`);
  }
  if (errors.length) {
    console.error(`grader error (not counted as kills):\n${errors.join('\n')}`);
    process.exit(2);
  }
  if (survivors.length) {
    console.error(`agent tests did not detect removal of the feature; surviving mutants: ${survivors.join(', ')}`);
    process.exit(1);
  }
  console.log(`agent tests killed every mutant: ${mutants.map(m => path.basename(m, '.json')).join(', ')}`);
}

if (require.main === module) main();
module.exports = { mask, replaceDeclarations, applyMutant, evaluate };
