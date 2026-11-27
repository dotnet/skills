// Staged only for grading. Workspace-state checks that stay valid for any file layout the agent picks.
//   retained   --dir <dir> --original <dir> --names A,B,...
//                                                 each named test still exists under <dir> with its original parameters,
//                                                 and every original attribute and top-level statement still appears in
//                                                 order (added attributes and statements are allowed). Comments and
//                                                 whitespace outside string and char literals are ignored; literal
//                                                 contents must match exactly.
//   added      --original <dir> --ext .cs,.csproj  no workspace file with these extensions is missing from <original>
//   no-scratch --projects <a.csproj>,<b.csproj>   no other .csproj, no script/HTTP probe files, and no .cs outside --code-dirs
//              [--code-dirs src,tests]
//   unchanged  --original <dir> --dir <dir> [--ignore-whitespace]
//                                                 <dir> holds exactly the original files with the same content; with
//                                                 --ignore-whitespace, C# whitespace outside literals is ignored
// Directories that contain a SKILL.md are skipped: they are staged skills, not agent output.
// Exit 0 when the check holds, 1 when it does not, 2 on a usage error.
const fs = require('fs');
const path = require('path');

const skipNames = new Set(['bin', 'obj', 'TestResults', '.git', '.grading', '.vs', 'node_modules']);
const scriptExtensions = new Set(['.py', '.sh', '.ps1', '.js', '.mjs', '.cjs', '.ts', '.http', '.rest', '.csx']);

function walk(dir, rel = '') {
  if (!fs.existsSync(dir)) return [];
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  // A staged skill directory is harness input, not agent output.
  if (rel && entries.some(entry => entry.isFile() && entry.name === 'SKILL.md')) return [];
  return entries.flatMap(entry => {
    if (skipNames.has(entry.name)) return [];
    const relPath = rel ? `${rel}/${entry.name}` : entry.name;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return walk(full, relPath);
    return entry.isFile() ? [relPath] : [];
  });
}

function options(argv) {
  const result = {};
  for (let i = 0; i < argv.length; i++) {
    const key = argv[i].replace(/^--/, '');
    const next = argv[i + 1];
    if (next === undefined || next.startsWith('--')) result[key] = true;
    else { result[key] = next; i++; }
  }
  return result;
}

const list = value => (typeof value === 'string' ? value.split(',').map(s => s.trim()).filter(Boolean) : []);

function retained({ dir, original, names }) {
  const methodsIn = root => walk(root).filter(f => f.endsWith('.cs')).flatMap(f => testMethods(fs.readFileSync(path.join(root, f), 'utf8')));
  const before = methodsIn(original);
  const after = methodsIn(dir);
  const problems = list(names).flatMap(name => {
    const expected = before.filter(m => m.name === name);
    if (expected.length !== 1) return [`'${name}' is not declared exactly once in the original tests (grader configuration error)`];
    const candidates = after.filter(m => m.name === name);
    if (!candidates.length) return [`${name} is no longer declared`];
    const reasons = candidates.map(current => preservationGap(expected[0], current));
    return reasons.some(reason => reason === null) ? [] : [`${name}: ${reasons[0]}`];
  });
  if (problems.length) return fail(`protected tests under ${dir} were not preserved:\n${problems.join('\n')}`);
  return pass(`all ${list(names).length} protected tests keep their original attributes, parameters and statements`);
}

// A protected test is preserved when its parameters are unchanged and every original attribute and
// statement still appears, in order; added attributes (for example new data rows) and statements are allowed.
function preservationGap(expected, current) {
  if (expected.parameters !== current.parameters) return `parameters changed from (${expected.parameters}) to (${current.parameters})`;
  const missingAttribute = firstMissingInOrder(expected.attributes, current.attributes);
  if (missingAttribute !== null) return `original attribute ${missingAttribute} is missing or out of order`;
  const missingStatement = firstMissingInOrder(expected.statements, current.statements);
  if (missingStatement !== null) return `original statement \`${missingStatement}\` is missing, changed or out of order`;
  return null;
}

function firstMissingInOrder(required, actual) {
  let next = 0;
  for (const item of required) {
    const found = actual.indexOf(item, next);
    if (found < 0) return item;
    next = found + 1;
  }
  return null;
}

// Classifies every character of C# source as comment, literal (string or char, delimiters included)
// or code. `structure` blanks comments and literals so delimiters inside them never count as code.
function scan(src) {
  const comment = new Array(src.length).fill(false);
  const literal = new Array(src.length).fill(false);
  const mark = (target, from, to) => { for (let k = from; k < Math.min(to, src.length); k++) target[k] = true; };
  for (let i = 0; i < src.length;) {
    const rest = src.slice(i, i + 4);
    if (rest.startsWith('//')) {
      const end = src.indexOf('\n', i) < 0 ? src.length : src.indexOf('\n', i);
      mark(comment, i, end); i = end;
    } else if (rest.startsWith('/*')) {
      const end = src.indexOf('*/', i + 2) < 0 ? src.length : src.indexOf('*/', i + 2) + 2;
      mark(comment, i, end); i = end;
    } else if (/^[$@]*"""/.test(rest)) {
      const open = src.indexOf('"""', i);
      const end = src.indexOf('"""', open + 3) < 0 ? src.length : src.indexOf('"""', open + 3) + 3;
      mark(literal, i, end); i = end;
    } else if (/^[$@]*"/.test(rest)) {
      const verbatim = /^\$?@|^@\$/.test(rest);
      let j = src.indexOf('"', i) + 1;
      while (j < src.length) {
        if (verbatim && src[j] === '"' && src[j + 1] === '"') { j += 2; continue; }
        if (!verbatim && src[j] === '\\') { j += 2; continue; }
        if (src[j] === '"') { j++; break; }
        j++;
      }
      mark(literal, i, j); i = j;
    } else if (src[i] === "'") {
      let j = i + 1;
      while (j < src.length && src[j] !== "'") j += src[j] === '\\' ? 2 : 1;
      mark(literal, i, j + 1); i = j + 1;
    } else {
      i++;
    }
  }
  const structure = src.split('').map((c, k) => (comment[k] || literal[k]) && c !== '\n' ? ' ' : c).join('');
  return { src, comment, literal, structure };
}

// Literal contents are kept exactly. Outside literals, whitespace survives only as one space between two
// word characters, and comments are dropped (or, with keepComments, kept with collapsed whitespace).
function normalize(scanned, from, to, keepComments = false) {
  const { src, comment, literal } = scanned;
  let out = '';
  let pendingSpace = false;
  for (let k = from; k < to; k++) {
    const c = src[k];
    if (literal[k]) {
      if (pendingSpace && /[\w@$]$/.test(out) && /[\w@$]/.test(c)) out += ' ';
      pendingSpace = false;
      out += c;
    } else if ((comment[k] && !keepComments) || /\s/.test(c)) {
      pendingSpace = true;
    } else {
      if (pendingSpace && /[\w@$]$/.test(out) && /[\w@$]/.test(c)) out += ' ';
      pendingSpace = false;
      out += c;
    }
  }
  return out;
}

// Top-level statements between `from` and `to`: split at `;` and at a closing brace that ends a block
// statement, never inside parentheses, brackets, braces, comments or literals.
function statements(scanned, from, to) {
  const { structure } = scanned;
  const result = [];
  let begin = from;
  let depth = 0;
  const flush = end => {
    const text = normalize(scanned, begin, end).replace(/;$/, '');
    if (text) result.push(text);
    begin = end;
  };
  for (let k = from; k < to; k++) {
    const c = structure[k];
    if ('([{'.includes(c)) depth++;
    else if (')]}'.includes(c)) {
      depth--;
      if (c === '}' && depth === 0) {
        let next = k + 1;
        while (next < to && /\s/.test(structure[next])) next++;
        if (!';),.'.includes(structure[next] ?? '')) flush(k + 1);
      }
    } else if (c === ';' && depth === 0) {
      flush(k + 1);
    }
  }
  flush(to);
  return result;
}

const modifier = /(?:^|[^\w@])(public|private|internal|protected|static|async|override|virtual|new|unsafe)$/;

// Each test-like method (void/Task/ValueTask return): its attribute groups, parameter list and top-level
// statements, each normalized. Modifiers and the return type may change (for example to async Task).
function testMethods(src) {
  const scanned = scan(src);
  const { structure } = scanned;
  const methods = [];
  const declaration = /\b(?:void|Task|ValueTask)\s+(\w+)\s*\(/g;
  for (let m; (m = declaration.exec(structure));) {
    const attributes = [];
    let start = m.index;
    for (;;) {
      let j = start;
      while (j > 0 && /\s/.test(structure[j - 1])) j--;
      const word = modifier.exec(structure.slice(Math.max(0, j - 16), j));
      if (word) { start = j - word[1].length; continue; }
      if (structure[j - 1] === ']') {
        let depth = 0, k = j - 1;
        for (; k >= 0; k--) {
          if (structure[k] === ']') depth++;
          else if (structure[k] === '[' && --depth === 0) break;
        }
        attributes.unshift(normalize(scanned, k, j));
        start = k;
        continue;
      }
      break;
    }
    const open = m.index + m[0].length;
    let i = open, depth = 1;
    for (; i < structure.length && depth > 0; i++) {
      if (structure[i] === '(') depth++;
      else if (structure[i] === ')') depth--;
    }
    const parameters = normalize(scanned, open, i - 1);
    while (i < structure.length && /\s/.test(structure[i])) i++;
    let end = -1;
    let body = [];
    if (structure.startsWith('=>', i)) {
      for (let k = i + 2, d = 0; k < structure.length; k++) {
        if ('([{'.includes(structure[k])) d++;
        else if (')]}'.includes(structure[k])) d--;
        else if (structure[k] === ';' && d === 0) { end = k + 1; break; }
      }
      if (end > 0) body = statements(scanned, i + 2, end);
    } else if (structure[i] === '{') {
      for (let k = i, d = 0; k < structure.length; k++) {
        if (structure[k] === '{') d++;
        else if (structure[k] === '}' && --d === 0) { end = k + 1; break; }
      }
      if (end > 0) body = statements(scanned, i + 1, end - 1);
    }
    if (end < 0) continue;
    methods.push({ name: m[1], attributes, parameters, statements: body });
    declaration.lastIndex = end;
  }
  return methods;
}

function added({ original, ext }) {
  const extensions = new Set(list(ext).map(e => e.toLowerCase()));
  const before = new Set(walk(original));
  const extra = walk('.').filter(f => extensions.has(path.extname(f).toLowerCase()) && !before.has(f));
  if (extra.length) return fail(`added ${[...extensions].join('/')} files:\n${extra.join('\n')}`);
  return pass(`no ${[...extensions].join('/')} files were added`);
}

function noScratch({ projects, 'code-dirs': codeDirs }) {
  const allowed = new Set(list(projects));
  const roots = list(codeDirs || 'src,tests');
  const files = walk('.');
  const problems = [
    ...files.filter(f => f.endsWith('.csproj') && !allowed.has(f)).map(f => `unexpected project ${f}`),
    ...files.filter(f => scriptExtensions.has(path.extname(f).toLowerCase())).map(f => `probe or script file ${f}`),
    ...files.filter(f => f.endsWith('.cs') && !roots.some(r => f.startsWith(`${r}/`))).map(f => `stray source file ${f}`),
  ];
  if (problems.length) return fail(problems.join('\n'));
  return pass('no scratch projects, probe scripts or stray sources');
}

function unchanged({ original, dir, 'ignore-whitespace': ignoreWhitespace }) {
  const canonical = (file, text) => {
    if (!ignoreWhitespace) return text.replace(/\r\n/g, '\n');
    if (!file.endsWith('.cs')) return text.replace(/\s+/g, '');
    const scanned = scan(text);
    return normalize(scanned, 0, text.length, true);
  };
  const expected = walk(original);
  const actual = new Set(walk(dir));
  const problems = [];
  for (const file of expected) {
    if (!actual.has(file)) { problems.push(`missing ${file}`); continue; }
    actual.delete(file);
    const before = canonical(file, fs.readFileSync(path.join(original, file), 'utf8'));
    const after = canonical(file, fs.readFileSync(path.join(dir, file), 'utf8'));
    if (before !== after) problems.push(`changed ${file}`);
  }
  for (const extra of actual) problems.push(`added ${extra}`);
  if (problems.length) return fail(`${dir} differs from its original${ignoreWhitespace ? ' beyond whitespace' : ''}:\n${problems.join('\n')}`);
  return pass(`${dir} matches its original${ignoreWhitespace ? ' apart from whitespace' : ''}`);
}

function pass(message) { console.log(message); return 0; }
function fail(message) { console.error(message); return 1; }

const commands = { retained, added, 'no-scratch': noScratch, unchanged };
const [command, ...rest] = process.argv.slice(2);
if (!commands[command]) {
  console.error(`usage: node check-workspace.js <${Object.keys(commands).join('|')}> [options]`);
  process.exit(2);
}
process.exit(commands[command](options(rest)));
