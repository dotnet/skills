// Staged only for grading. Usage: node assert-single-parser.js [src-dir]
// Fails (exit 1) when duplicated parsing survives or the shipped AppSettingsHelper and ConfigReader
// entry points do not reach the same AppSettingsHelper implementation. That implementation may be
// one int and one bool parse call, or one generic `T.TryParse`/`T.Parse` core. Comments and
// string/char literals are masked before inspection.
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
const count = type => (code.match(new RegExp(
  `(?<![\\w.])(?:global\\s*::\\s*)?(?:System\\s*\\.\\s*)?(?:${type})\\s*\\.\\s*(?:TryParse|Parse)\\b`, 'g')) || []).length;
const ints = count('int|Int32');
const bools = count('bool|Boolean');

function typeBody(name) {
  const declaration = new RegExp(`\\bclass\\s+${name}\\b[^{}]*\\{`, 'g');
  const bodies = [];
  for (const match of code.matchAll(declaration)) {
    const start = match.index + match[0].length - 1;
    let depth = 0;
    for (let i = start; i < code.length; i++) {
      if (code[i] === '{') depth++;
      else if (code[i] === '}' && --depth === 0) {
        bodies.push(code.slice(start + 1, i));
        break;
      }
    }
  }
  return bodies.length ? bodies.join('\n') : null;
}

function memberBody(source, name, returnType) {
  if (!source) return null;
  const declaration = new RegExp(
    `\\b(?:(?:public|private|internal|protected)\\s+)?static\\s+${returnType}\\s+${name}\\s*\\([^)]*\\)\\s*(\\{|=>)`, 'g');
  return bodyAt(source, declaration.exec(source));
}

function bodyAt(source, match) {
  if (!match) return null;
  const opener = match[match.length - 1];
  const start = match.index + match[0].length - opener.length;
  if (opener === '=>') {
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

function callsAppSettingsMember(body, member) {
  if (!body) return false;
  const qualifiers = ['AppSettingsHelper', ...aliases]
    .map(name => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('|');
  return new RegExp(`\\b(?:${qualifiers})\\s*\\.\\s*${member}\\b`).test(body) ||
    (hasStaticImport && new RegExp(`\\b${member}\\b`).test(body));
}

function memberNames(source, returnType) {
  const declaration = new RegExp(
    `\\b(?:(?:public|private|internal|protected)\\s+)?static\\s+${returnType}\\s+([A-Za-z_]\\w*)\\s*\\(`, 'g');
  return [...source.matchAll(declaration)].map(match => match[1]);
}

// A generic core such as `static T ParseSetting<T>(string? raw, T fallback) where T : IParsable<T>`
// is one parsing implementation for every setting type: it calls `T.TryParse`/`T.Parse` on its
// type parameter instead of `int.`/`bool.`. Returns each such core's name and parse-call count.
function genericParsers(source) {
  if (!source) return [];
  const declaration =
    /\bstatic\s+[\w.?\[\]]+\s+([A-Za-z_]\w*)\s*<\s*([A-Za-z_]\w*)\s*>\s*\([^)]*\)[^{;]*?(\{|=>)/g;
  return [...source.matchAll(declaration)].flatMap(match => {
    const calls = (bodyAt(source, match) || '').match(
      new RegExp(`(?<![\\w.])${match[2]}\\s*\\.\\s*(?:TryParse|Parse)\\b`, 'g'));
    return calls ? [{ name: match[1], calls: calls.length }] : [];
  });
}

function reachesParser(body, returnType, parses, seen = new Set()) {
  if (!body) return false;
  if (parses(body)) return true;

  for (const match of body.matchAll(/\b([A-Za-z_]\w*)\s*(?:<[^<>();]*>\s*)?\(/g)) {
    const name = match[1];
    if (sharedGenerics.has(name)) return true;
    if (seen.has(name)) continue;
    const target = memberBody(appSettings, name, returnType);
    if (!target) continue;
    const nextSeen = new Set(seen).add(name);
    if (reachesParser(target, returnType, parses, nextSeen)) return true;
  }
  return false;
}

function forwardsToSharedParser(body, returnType, parses) {
  if (!body || parses(body)) return false;
  if ([...sharedGenerics].some(name => callsAppSettingsMember(body, name))) return true;
  return memberNames(appSettings, returnType).some(name =>
    callsAppSettingsMember(body, name) &&
    reachesParser(memberBody(appSettings, name, returnType), returnType, parses, new Set([name])));
}

const intType = '(?:int|Int32|System\\s*\\.\\s*Int32)';
const boolType = '(?:bool|Boolean|System\\s*\\.\\s*Boolean)';
const appSettings = typeBody('AppSettingsHelper');
const configReader = typeBody('ConfigReader');
// Generic cores anywhere count toward duplication; only AppSettingsHelper's are shared targets.
const generics = genericParsers(code).reduce((sum, parser) => sum + parser.calls, 0);
const sharedGenerics = new Set(genericParsers(appSettings).map(parser => parser.name));
const parseInt = memberBody(appSettings, 'ParseIntSetting', intType);
const parseBool = memberBody(appSettings, 'ParseBoolSetting', boolType);
const readInt = memberBody(configReader, 'ReadInt', intType);
const readBool = memberBody(configReader, 'ReadBool', boolType);
const parsesInt = body => body && /(?<![\w.])(?:System\s*\.\s*)?(?:int|Int32)\s*\.\s*(?:TryParse|Parse)\b/.test(body);
const parsesBool = body => body && /(?<![\w.])(?:System\s*\.\s*)?(?:bool|Boolean)\s*\.\s*(?:TryParse|Parse)\b/.test(body);
const errors = [];
// Exactly one implementation: either one int and one bool parse call, or one generic core and none.
const concreteShape = ints === 1 && bools === 1 && generics === 0;
const genericShape = generics === 1 && ints === 0 && bools === 0;
if (!concreteShape && !genericShape) {
  errors.push(`expected one parsing implementation: ${ints} int parser call(s), ${bools} bool parser call(s), ` +
    `${generics} generic parser call(s)`);
}
if (!reachesParser(parseInt, intType, parsesInt)) {
  errors.push('AppSettingsHelper.ParseIntSetting does not reach the shared int parser');
}
if (!reachesParser(parseBool, boolType, parsesBool)) {
  errors.push('AppSettingsHelper.ParseBoolSetting does not reach the shared bool parser');
}
if (!forwardsToSharedParser(readInt, intType, parsesInt)) {
  errors.push('ConfigReader.ReadInt does not forward to the shared AppSettingsHelper int parser');
}
if (!forwardsToSharedParser(readBool, boolType, parsesBool)) {
  errors.push('ConfigReader.ReadBool does not forward to the shared AppSettingsHelper bool parser');
}
if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log(`single parsing implementation: ${ints} int, ${bools} bool, ${generics} generic parser call(s)`);
