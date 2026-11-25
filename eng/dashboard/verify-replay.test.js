const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const script = path.join(__dirname, 'verify-replay.js');
const revision = 'd546084ba55319b26da1353b3c2cd30564ca631b';

function makeViewer(t) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'replay-viewer-'));
  const assets = path.join(directory, 'assets');
  fs.mkdirSync(assets);
  fs.writeFileSync(path.join(directory, '.agentviz-commit'), `${revision}\n`);
  fs.writeFileSync(path.join(directory, 'index.html'), '<script type="module" src="./assets/index.js"></script>');
  fs.writeFileSync(path.join(assets, 'index.js'), 'export {};\n');
  t.after(() => {
    for (const file of [
      path.join(directory, '.agentviz-commit'),
      path.join(directory, 'index.html'),
      path.join(assets, 'index.js'),
    ]) {
      if (fs.existsSync(file)) fs.unlinkSync(file);
    }
    fs.rmdirSync(assets);
    fs.rmdirSync(directory);
  });
  return directory;
}

function verify(directory) {
  return spawnSync(process.execPath, [script, directory, 'skills'], { encoding: 'utf8' });
}

test('accepts an existing viewer with its referenced entry script', t => {
  const result = verify(makeViewer(t));
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, new RegExp(revision));
});

test('rejects an incomplete viewer instead of publishing with a broken replay', t => {
  const directory = makeViewer(t);
  fs.unlinkSync(path.join(directory, 'assets', 'index.js'));
  const result = verify(directory);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /::error title=Existing replay viewer invalid::/);
});

test('rejects a viewer without a valid revision marker', t => {
  const directory = makeViewer(t);
  fs.writeFileSync(path.join(directory, '.agentviz-commit'), 'unknown\n');
  const result = verify(directory);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /no valid revision marker/);
});

test('rejects entry scripts outside the deployed viewer', t => {
  const directory = makeViewer(t);
  fs.writeFileSync(path.join(directory, 'index.html'), '<script src="../../other.js"></script>');
  const result = verify(directory);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /outside the viewer/);
});
