// Staged only for grading. Usage: node assert-test-coverage.js <hex|platform>
// Proves the agent wrote tests for the requested feature without dictating file names or
// assertion style: some agent-authored C# file under tests/ (grader-owned GraderChecks and build
// output excluded) must reference the member and every required input/expected literal in code,
// not in comments. This is only a cheap static gate: assert-tests-kill-mutants.js proves the
// agent's tests actually fail when the feature is removed, and GraderChecks grade behavior.
const fs = require('fs');

// Blank out // and /* */ comments while keeping string and char literals intact.
function stripComments(src) {
  let out = '';
  for (let i = 0; i < src.length;) {
    const c = src[i], n = src[i + 1];
    if (c === '/' && n === '/') {
      while (i < src.length && src[i] !== '\n') i++;
    } else if (c === '/' && n === '*') {
      const end = src.indexOf('*/', i + 2);
      i = end < 0 ? src.length : end + 2;
      out += ' ';
    } else if (src.startsWith('"""', i)) {
      let q = 0;
      while (src[i + q] === '"') q++;
      const close = src.indexOf('"'.repeat(q), i + q);
      const end = close < 0 ? src.length : close + q;
      out += src.slice(i, end);
      i = end;
    } else if (c === '"' || c === "'") {
      const verbatim = c === '"' && (src[i - 1] === '@' || (src[i - 1] === '$' && src[i - 2] === '@'));
      let j = i + 1;
      while (j < src.length) {
        if (verbatim && src[j] === '"' && src[j + 1] === '"') { j += 2; continue; }
        if (!verbatim && src[j] === '\\') { j += 2; continue; }
        if (src[j] === c || (!verbatim && src[j] === '\n')) { j++; break; }
        j++;
      }
      out += src.slice(i, j);
      i = j;
    } else {
      out += c;
      i++;
    }
  }
  return out;
}

const checks = {
  hex: { member: /\bParseIntSetting\b/, literals: [/"0[xX][0-9A-Fa-f]+"/] },
  platform: { member: /\bPlatformInfo\s*\.\s*Tag\s*\(\s*\)/, literals: [/"net8"/, /"net10"/] },
};
const check = checks[process.argv[2]];
if (!check) {
  console.error(`unknown check '${process.argv[2]}'; expected one of ${Object.keys(checks)}`);
  process.exit(2);
}

const skip = new Set(['bin', 'obj', 'TestResults', 'GraderChecks']);
const walk = d => !fs.existsSync(d) ? [] : fs.readdirSync(d, { withFileTypes: true })
  .flatMap(e => e.isDirectory()
    ? (skip.has(e.name) ? [] : walk(`${d}/${e.name}`))
    : e.name.endsWith('.cs') ? [`${d}/${e.name}`] : []);

const covering = walk('tests').filter(f => {
  const text = stripComments(fs.readFileSync(f, 'utf8'));
  return check.member.test(text) && check.literals.every(l => l.test(text));
});
if (!covering.length) {
  console.error(`no agent-authored test covers ${process.argv[2]}`);
  process.exit(1);
}
console.log('covered by:', covering.join(', '));
