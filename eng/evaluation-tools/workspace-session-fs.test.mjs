import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdir, mkdtemp, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { LocalSessionFsHandler } from './node_modules/@microsoft/vally/dist/executor/local-session-fs-handler.js';
import { withWorkspaceReads } from './workspace-session-fs.mjs';

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
    const provider = withWorkspaceReads(original, workspace);
    assert.equal(await provider.exists(file), true);
    assert.equal((await provider.stat(file)).isFile, true);
    assert.equal(await provider.readFile(file), 'actual reference\n');
    assert.deepEqual(await provider.readdir(workspace), ['reference.md']);
    assert.deepEqual(await provider.readdirWithTypes(workspace), [{ name: 'reference.md', type: 'file' }]);
    assert.equal(await provider.exists(path.join(workspace, 'missing.md')), false);
    await assert.rejects(provider.readFile(path.join(workspace, 'missing.md')), { code: 'ENOENT' });
    await assert.rejects(provider.readFile(path.join(workspace, '..', 'outside.md')), /escapes root/);
    await assert.rejects(provider.writeFile(file, 'overwritten'), /escapes root/);
    assert.equal(await readFile(file, 'utf8'), 'actual reference\n');

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
    await assert.rejects(provider.readFile(path.join(workspace, 'escape.md')), /Workspace read escapes root/);
    await assert.rejects(provider.exists(path.join(workspace, 'escape.md')), /Workspace read escapes root/);
    await symlink(path.join(workspace, 'missing.md'), path.join(workspace, 'missing-link.md'));
    assert.equal(await provider.exists(path.join(workspace, 'missing-link.md')), false);
  } finally {
    await rm(root, { recursive: true });
  }
});
