const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const expected = {
  'NuGet.Config': 'b8ff5486232770da7b6bced4a4b829b7c9dc184961bad1c2b8c965f46f584b90',
  'affected-workspace/global.json': 'c9c9cae030d639a70a7f7642ee546c9a31dda4bae6492a3a8aa21d08cab83779',
  'affected-workspace/NuGet.Config': '3c086b57ae3b6b33cc26b35fd7c700db86f26e566976383796f82bbafb8d7fff',
  'affected-workspace/host.json': '09432624e12b77b7de4065aee62d6464860b8dc83fdaca8ab7fdc46ac2e6123e',
  'affected-workspace/dotnet.cjs': '68e972803b765d4b681cef76a23476bdc1e8adb17beffb3d484c11d826058edc',
};
for (const [file, hash] of Object.entries(expected)) {
  const content = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
  assert.equal(crypto.createHash('sha256').update(content).digest('hex'), hash,
    'Changed or missing diagnostic input: ' + file);
}
const cwd = fs.realpathSync.native('affected-workspace');
const records = fs.readFileSync(path.join(cwd, 'launch-invocations.jsonl'), 'utf8')
  .trim().split(/\r?\n/).map(line => JSON.parse(line));
const launches = records.filter(record => record.argv[0] === 'dnx');
assert.equal(launches.length, 1, 'Expected one actual launch, not search-only advice');
const launch = launches[0];
for (const record of records) {
  assert.equal(fs.realpathSync.native(record.cwd), cwd,
    'Launch must use the affected workspace, including SDK/feed diagnostics');
}
assert.ok(records.some(record => ['--version', '--info'].includes(record.argv[0])),
  'Missing workspace SDK inspection');
assert.ok(records.some(record => record.argv.join(' ') === 'nuget list source'),
  'Missing workspace feed inspection');
assert.deepEqual(launch.argv,
  ['dnx', 'Microsoft.AITools.BinlogMcp', '--yes', '--prerelease'],
  'Launch arguments must match the registered command without source overrides');
assert.equal(launch.exitCode, 1);
const result = JSON.parse(fs.readFileSync(path.join(cwd, 'launch-result.json'), 'utf8'));
for (const key of ['stdout', 'stderr', 'exitCode']) {
  assert.equal(result[key], launch[key], 'Missing or inaccurate captured ' + key);
}
assert.match(result.stderr, /not found in NuGet feeds approved-mirror/);
