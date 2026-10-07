import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdir, mkdtemp, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { LocalSessionFsHandler } from './node_modules/@microsoft/vally/dist/executor/local-session-fs-handler.js';
import { withWorkspaceAccess } from './workspace-session-fs.mjs';

test('native workspace readers preserve session-root confinement and log writes', async () => {
  const root = await mkdtemp(path.join(process.cwd(), '.workspace-fs-test-'));
  try {
    const workspace = path.join(root, 'workspace');
    const logs = path.join(root, 'logs');
    await mkdir(workspace);
    await mkdir(logs);
    const file = path.join(workspace, 'reference.md');
    await writeFile(file, 'actual reference\n');
    const original = new LocalSessionFsHandler(logs);
    await assert.rejects(original.stat(file), /escapes root/);
    const provider = withWorkspaceAccess(original, workspace);
    assert.equal(await provider.exists(file), true);
    assert.equal((await provider.stat(file)).isFile, true);
    assert.equal(await provider.readFile(file), 'actual reference\n');
    assert.deepEqual(await provider.readdir(workspace), ['reference.md']);
    assert.deepEqual(await provider.readdirWithTypes(workspace), [{ name: 'reference.md', type: 'file' }]);
    assert.equal(await provider.exists(path.join(workspace, 'missing.md')), false);
    await assert.rejects(provider.readFile(path.join(workspace, 'missing.md')), { code: 'ENOENT' });
    await assert.rejects(provider.readFile(path.join(workspace, '..', 'outside.md')), /escapes root/);
    await provider.writeFile(file, 'updated reference\n');
    assert.equal(await readFile(file, 'utf8'), 'updated reference\n');

    await provider.writeFile('events.jsonl', 'first\n');
    await provider.appendFile('events.jsonl', 'second\n');
    assert.equal(await provider.readFile('events.jsonl'), 'first\nsecond\n');
    assert.equal(await provider.readFile(path.join(logs, 'events.jsonl')), 'first\nsecond\n');
    await provider.rename('events.jsonl', 'renamed.jsonl');
    await provider.rm('renamed.jsonl', false, false);
    assert.equal(await provider.exists('renamed.jsonl'), false);

    const outside = path.join(root, 'outside.md');
    await writeFile(outside, 'not a trial input\n');
    await symlink(outside, path.join(workspace, 'escape.md'));
    await assert.rejects(provider.readFile(path.join(workspace, 'escape.md')), /escapes root/);
    await assert.rejects(provider.exists(path.join(workspace, 'escape.md')), /escapes root/);
    await symlink(path.join(workspace, 'missing.md'), path.join(workspace, 'missing-link.md'));
    assert.equal(await provider.exists(path.join(workspace, 'missing-link.md')), false);
  } finally {
    await rm(root, { recursive: true });
  }
});

test('native workspace mutations preserve boundaries and symlink entry semantics', async () => {
  const root = await mkdtemp(path.join(process.cwd(), '.workspace-fs-test-'));
  try {
    const workspace = path.join(root, 'workspace');
    const logs = path.join(root, 'logs');
    const outside = path.join(root, 'outside');
    await Promise.all([workspace, logs, outside].map(file => mkdir(file)));
    const original = new LocalSessionFsHandler(logs);
    const provider = withWorkspaceAccess(original, workspace);
    const file = path.join(workspace, 'new', 'Probe.txt');
    await assert.rejects(original.writeFile(file, 'blocked'), /escapes root/);
    await provider.writeFile(file, 'one');
    await provider.appendFile(file, ' two');
    assert.equal(await provider.readFile(file), 'one two');
    const renamed = path.join(workspace, 'other', 'Renamed.txt');
    await provider.rename(file, renamed);
    assert.equal(await provider.readFile(renamed), 'one two');
    await provider.rm(renamed, false, false);
    assert.equal(await provider.exists(renamed), false);
    assert.equal(await provider.mkdir(path.join(workspace, 'deep', 'directory'), true), undefined);
    await provider.rm(path.join(workspace, 'already-missing'), false, true);
    await provider.writeFile(path.join(logs, 'absolute.log'), 'log');
    assert.equal(await readFile(path.join(logs, 'absolute.log'), 'utf8'), 'log');
    await assert.rejects(provider.writeFile(path.join(outside, 'escape'), 'bad'), /escapes root/);
    await writeFile(path.join(outside, 'input'), 'protected');
    await symlink(outside, path.join(workspace, 'outside-link'));
    await assert.rejects(provider.writeFile(path.join(workspace, 'outside-link', 'new'), 'bad'),
      /escapes root/);
    await assert.rejects(provider.appendFile(path.join(workspace, 'outside-link', 'input'), 'bad'),
      /escapes root/);
    await assert.rejects(provider.mkdir(path.join(workspace, 'outside-link', 'new'), true),
      /escapes root/);
    await assert.rejects(provider.rm(path.join(workspace, 'outside-link', 'input'), false, false),
      /escapes root/);
    await assert.rejects(provider.rename(path.join(workspace, 'outside-link', 'input'),
      path.join(workspace, 'stolen')), /escapes root/);
    await provider.writeFile(path.join(workspace, 'safe'), 'safe');
    await assert.rejects(provider.rename(path.join(workspace, 'safe'),
      path.join(workspace, 'outside-link', 'stolen')), /escapes root/);
    await assert.rejects(provider.rename(path.join(workspace, 'safe'),
      path.join(logs, 'cross-root')), /escapes root/);
    await symlink(path.join(outside, 'missing-target'), path.join(workspace, 'dangling'));
    await assert.rejects(provider.writeFile(path.join(workspace, 'dangling'), 'bad'), /symlink/);
    const link = path.join(workspace, 'inside-link');
    await symlink(path.join(workspace, 'safe'), link);
    await provider.rename(link, path.join(workspace, 'renamed-link'));
    assert.equal(await provider.readFile(path.join(workspace, 'safe')), 'safe');
    await provider.rm(path.join(workspace, 'renamed-link'), false, false);
    assert.equal(await provider.readFile(path.join(workspace, 'safe')), 'safe');
    await assert.rejects(provider.rm(workspace, true, true), /workspace root/);
    await assert.rejects(provider.rename(workspace, path.join(workspace, 'child')), /workspace root/);
    await assert.rejects(provider.rename(path.join(workspace, 'safe'), workspace), /workspace root/);
    assert.equal(await readFile(path.join(outside, 'input'), 'utf8'), 'protected');
  } finally {
    await rm(root, { recursive: true });
  }
});
