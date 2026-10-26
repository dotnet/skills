// Staged only for grading. Usage: node assert-single-parser.js [src-dir]
// Fails (exit 1) when duplicated parsing survives or ConfigReader does not forward both entry
// points to AppSettingsHelper. Comments and string/char literals are masked before inspection.
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

function typeBody(name) {
  const declaration = new RegExp(`\\bclass\\s+${name}\\b[^{}]*\\{`, 'g');
  const match = declaration.exec(code);
  if (!match) return null;
  const start = match.index + match[0].length - 1;
  let depth = 0;
  for (let i = start; i < code.length; i++) {
    if (code[i] === '{') depth++;
    else if (code[i] === '}' && --depth === 0) return code.slice(start + 1, i);
  }
  return null;
}

function memberBody(source, name, returnType) {
  if (!source) return null;
  const declaration = new RegExp(
    `\\b(?:public\\s+)?static\\s+${returnType}\\s+${name}\\s*\\([^)]*\\)\\s*(\\{|=>)`, 'g');
  const match = declaration.exec(source);
  if (!match) return null;
  const start = match.index + match[0].length - match[1].length;
  if (match[1] === '=>') {
    let depth = 0;
    for (let i = start + 2; i < source.length; i++) {
      if ('([{'.includes(source[i])) depth++;
      else if (')]}'.includes(source[i])) depth--;
      else if (source[i] === ';' && depth === 0) return source.slice(start + 2, i);
    }
    return null;
  }

  let depth = 0;
  for (let i = start; i < source.length; i++) {
    if (source[i] === '{') depth++;
    else if (source[i] === '}' && --depth === 0) return source.slice(start + 1, i);
  }
  return null;
}

const aliases = [...code.matchAll(
  /\busing\s+([A-Za-z_]\w*)\s*=\s*(?:global\s*::\s*)?(?:Billing\s*\.\s*)?AppSettingsHelper\s*;/g)]
  .map(match => match[1]);
const hasStaticImport =
  /\busing\s+static\s+(?:global\s*::\s*)?(?:Billing\s*\.\s*)?AppSettingsHelper\s*;/.test(code);

function callsShared(body, member) {
  if (!body) return false;
  const qualifiers = ['AppSettingsHelper', ...aliases]
    .map(name => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('|');
  return new RegExp(`\\b(?:${qualifiers})\\s*\\.\\s*${member}\\b`).test(body) ||
    (hasStaticImport && new RegExp(`\\b${member}\\b`).test(body));
}

const configReader = typeBody('ConfigReader');
const readInt = memberBody(configReader, 'ReadInt', '(?:int|Int32|System\\s*\\.\\s*Int32)');
const readBool = memberBody(configReader, 'ReadBool', '(?:bool|Boolean|System\\s*\\.\\s*Boolean)');
const parsesInt = body => body && /(?<![\w.])(?:System\s*\.\s*)?(?:int|Int32)\s*\.\s*(?:TryParse|Parse)\b/.test(body);
const parsesBool = body => body && /(?<![\w.])(?:System\s*\.\s*)?(?:bool|Boolean)\s*\.\s*(?:TryParse|Parse)\b/.test(body);
const errors = [];
if (ints > 1 || bools > 1) {
  errors.push(`duplicated parsing remains: ${ints} int TryParse call(s), ${bools} bool TryParse call(s)`);
}
if (!callsShared(readInt, 'ParseIntSetting') || parsesInt(readInt)) {
  errors.push('ConfigReader.ReadInt does not forward exclusively to AppSettingsHelper.ParseIntSetting');
}
if (!callsShared(readBool, 'ParseBoolSetting') || parsesBool(readBool)) {
  errors.push('ConfigReader.ReadBool does not forward exclusively to AppSettingsHelper.ParseBoolSetting');
}
if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log(`single parsing implementation: ${ints} int, ${bools} bool TryParse call(s)`);
