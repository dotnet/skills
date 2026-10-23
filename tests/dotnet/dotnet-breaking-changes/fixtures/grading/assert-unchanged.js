// Staged only for grading. Usage: node assert-unchanged.js <manifest.json>
// Fails when any manifest file differs from its starting content (line endings normalized),
// is missing, or when a file not in the manifest appears under the manifest's roots or as a
// build/configuration input at the workspace root. `graderFiles` lists the exact files the
// stimulus's grading_environment injects; nothing else is exempt. `renames` maps a manifest file
// to the one requested identifier rename ({ from, to }): that file must no longer contain `from`
// and, with `to` mapped back to `from`, must match its starting content exactly.
const fs = require('fs');
const crypto = require('crypto');

const manifest = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const graderFiles = new Set(manifest.graderFiles || []);
const renames = manifest.renames || {};
const word = w => new RegExp(`\\b${w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\b`, 'g');
const content = p => {
  const text = fs.readFileSync(p, 'utf8').replace(/\r\n/g, '\n');
  const r = renames[p];
  if (!r) return text;
  if (word(r.from).test(text)) return null;
  return text.replace(word(r.to), r.from);
};
const hash = p => {
  const text = content(p);
  return text === null ? null : crypto.createHash('sha256').update(text).digest('hex');
};
const skip = new Set(['bin', 'obj', 'TestResults']);
const walk = d => !fs.existsSync(d) ? [] : fs.readdirSync(d, { withFileTypes: true })
  .flatMap(e => e.isDirectory()
    ? (skip.has(e.name) ? [] : walk(`${d}/${e.name}`))
    : [`${d}/${e.name}`]);

const changed = Object.keys(manifest.files)
  .filter(p => !fs.existsSync(p) || hash(p) !== manifest.files[p]);
const unexpected = manifest.roots.flatMap(walk)
  .filter(p => !(p in manifest.files) && !graderFiles.has(p));
// MSBuild, NuGet, and the compiler also pick up configuration from the workspace root, so a new
// root Directory.Build.props, NuGet.config, .editorconfig, etc. changes every project even though
// src/ and tests/ are untouched. Only build inputs are matched; the harness-owned .grading
// directory and unrelated root files are not.
const rootBuildInput = /^(directory\.(build|packages|solution)\.(props|targets|rsp)|nuget\.config|\.editorconfig|\.?[^/]*\.globalconfig|global\.json|[^/]*\.(props|targets|rsp|sln|slnx|slnf|csproj))$/i;
const unexpectedRoot = fs.readdirSync('.', { withFileTypes: true })
  .filter(e => e.isFile() && rootBuildInput.test(e.name) && !(e.name in manifest.files))
  .map(e => e.name);
unexpected.push(...unexpectedRoot);
if (changed.length || unexpected.length) {
  console.error('changed or missing:', changed, 'unexpected:', unexpected);
  process.exit(1);
}
console.log('workspace unchanged');
