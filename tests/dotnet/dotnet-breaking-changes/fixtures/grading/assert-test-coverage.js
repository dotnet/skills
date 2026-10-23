// Staged only for grading. Usage: node assert-test-coverage.js <hex|platform>
// Proves the agent wrote tests for the requested feature without dictating file names or
// assertion style: some agent-authored C# file under tests/ (grader-owned GraderChecks and build
// output excluded) must reference the member and every required input/expected literal.
// Behavior itself is graded separately by the executable GraderChecks.
const fs = require('fs');

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
  const text = fs.readFileSync(f, 'utf8');
  return check.member.test(text) && check.literals.every(l => l.test(text));
});
if (!covering.length) {
  console.error(`no agent-authored test covers ${process.argv[2]}`);
  process.exit(1);
}
console.log('covered by:', covering.join(', '));
