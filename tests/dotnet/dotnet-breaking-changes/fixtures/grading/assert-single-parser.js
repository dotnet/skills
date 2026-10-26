// Staged only for grading. Usage: node assert-single-parser.js [src-dir]
// Fails (exit 1) when src/Billing code still holds more than one int or bool TryParse call, i.e.
// duplicated parsing survived the consolidation. Comments and string/char literals are masked
// first, so a forwarder documented as "// Preserve int.TryParse behavior" is not counted as a
// second implementation. `int`/`Int32` and `bool`/`Boolean` spellings count alike.
const fs = require('fs');
const path = require('path');
const { mask } = require('./assert-tests-kill-mutants.js');

const csFiles = dir => !fs.existsSync(dir) ? [] : fs.readdirSync(dir, { withFileTypes: true })
  .flatMap(e => {
    if (['bin', 'obj'].includes(e.name)) return [];
    const p = path.join(dir, e.name);
    return e.isDirectory() ? csFiles(p) : e.name.endsWith('.cs') ? [p] : [];
  });

const code = csFiles(process.argv[2] || 'src/Billing').map(f => mask(fs.readFileSync(f, 'utf8'))).join('\n');
const count = type => (code.match(new RegExp(`(?<![\\w.])(?:global\\s*::\\s*)?(?:System\\s*\\.\\s*)?(?:${type})\\s*\\.\\s*TryParse\\b`, 'g')) || []).length;
const ints = count('int|Int32');
const bools = count('bool|Boolean');
if (ints > 1 || bools > 1) {
  console.error(`duplicated parsing remains: ${ints} int TryParse call(s), ${bools} bool TryParse call(s)`);
  process.exit(1);
}
console.log(`single parsing implementation: ${ints} int, ${bools} bool TryParse call(s)`);
