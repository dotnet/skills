import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdir, mkdtemp, readFile, rename, rm, symlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createSessionFsAdapter } from '@github/copilot-sdk';
import { LocalSessionFsHandler } from './node_modules/@microsoft/vally/dist/executor/local-session-fs-handler.js';
import { withWorkspaceIsolationGuard } from './workspace-session-fs.mjs';

const rejection = { code: 'ERR_EVALUATION_WORKSPACE_ISOLATION_REQUIRED' };

test('workspace operations fail closed while session-log operations retain original behavior', async () => {
  const root = await mkdtemp(path.join(process.cwd(), '.workspace-fs-test-'));
  try {
    const workspace = path.join(root, 'workspace');
    const logs = path.join(root, 'logs');
    await Promise.all([workspace, logs].map(file => mkdir(file)));
    const file = path.join(workspace, 'reference.md');
    await writeFile(file, 'actual reference\n');
    const original = new LocalSessionFsHandler(logs);
    const provider = withWorkspaceIsolationGuard(original, workspace);
    await assert.rejects(original.readFile(file), /escapes root/);
    for (const [operation, args] of [
      ['readFile', [file]], ['stat', [file]], ['exists', [file]],
      ['readdir', [workspace]], ['readdirWithTypes', [workspace]],
      ['writeFile', [file, 'blocked']], ['appendFile', [file, 'blocked']],
      ['mkdir', [path.join(workspace, 'new'), true]], ['rm', [file, false, false]],
      ['rename', [file, path.join(workspace, 'renamed')]],
      ['rename', ['events.jsonl', file]], ['rename', [file, 'events.jsonl']],
      ['rename', [path.join(logs, 'events.jsonl'), file]],
      ['rm', [workspace, true, true]],
    ]) {
      await assert.rejects(provider[operation](...args), rejection);
    }
    await assert.rejects(provider.exists(path.join(workspace, 'missing')), rejection);
    const adapter = createSessionFsAdapter(provider);
    for (const result of [
      (await adapter.readFile({ path: file })).error,
      (await adapter.stat({ path: file })).error,
      await adapter.writeFile({ path: file, content: 'blocked' }),
    ]) {
      assert.equal(result.code, 'UNKNOWN');
      assert.match(result.message, /disabled.*atomically confine/);
    }
    assert.deepEqual(await adapter.exists({ path: file }), { exists: false });
    await assert.rejects(provider.readFile(path.join(root, 'outside')), /escapes root/);
    assert.equal(await readFile(file, 'utf8'), 'actual reference\n');

    await provider.writeFile('events.jsonl', 'first\n');
    await provider.appendFile(path.join(logs, 'events.jsonl'), 'second\n');
    assert.equal(await provider.readFile('events.jsonl'), 'first\nsecond\n');
    assert.equal((await provider.stat(path.join(logs, 'events.jsonl'))).isFile, true);
    assert.deepEqual(await provider.readdir(logs), ['events.jsonl']);
    assert.deepEqual(await provider.readdirWithTypes(logs), [{ name: 'events.jsonl', type: 'file' }]);
    await provider.mkdir('nested', true);
    await provider.rename('events.jsonl', 'nested/renamed.jsonl');
    await provider.rm('nested/renamed.jsonl', false, false);
    await provider.rm('nested', true, false);
    assert.equal(await provider.exists('nested'), false);

    const nestedLogs = path.join(workspace, 'session-logs');
    await mkdir(nestedLogs);
    const nestedProvider = withWorkspaceIsolationGuard(new LocalSessionFsHandler(nestedLogs), workspace);
    const nestedLog = path.join(nestedLogs, 'events.jsonl');
    await nestedProvider.writeFile(nestedLog, 'nested log');
    assert.equal(await nestedProvider.readFile(nestedLog), 'nested log');
    await assert.rejects(nestedProvider.readFile(file), rejection);
  } finally {
    await rm(root, { recursive: true });
  }
});

test('intermediate-parent swaps cannot redirect rejected workspace reads or mutations', async () => {
  const root = await mkdtemp(path.join(process.cwd(), '.workspace-fs-test-'));
  try {
    const workspace = path.join(root, 'workspace');
    const logs = path.join(root, 'logs');
    const outside = path.join(root, 'outside');
    const parent = path.join(workspace, 'parent');
    const saved = path.join(workspace, 'saved-parent');
    await Promise.all([parent, logs, outside].map(file => mkdir(file, { recursive: true })));
    const file = path.join(parent, 'owned.txt');
    const outsideFile = path.join(outside, 'owned.txt');
    await writeFile(file, 'inside');
    await writeFile(outsideFile, 'outside');
    const provider = withWorkspaceIsolationGuard(new LocalSessionFsHandler(logs), workspace);
    for (const [operation, args] of [
      ['readFile', [file]], ['writeFile', [file, 'blocked']],
      ['appendFile', [file, 'blocked']], ['stat', [file]], ['exists', [file]],
      ['readdir', [parent]], ['readdirWithTypes', [parent]],
      ['mkdir', [path.join(parent, 'new'), true]], ['rm', [file, false, false]],
      ['rename', [file, path.join(parent, 'renamed')]],
    ]) {
      const pending = assert.rejects(provider[operation](...args), rejection);
      await rename(parent, saved);
      await symlink(outside, parent);
      await pending;
      await assert.rejects(provider[operation](...args), rejection);
      assert.equal(await readFile(outsideFile, 'utf8'), 'outside');
      assert.deepEqual(await provider.readdir(logs), []);
      await rm(parent);
      await rename(saved, parent);
      assert.equal(await readFile(file, 'utf8'), 'inside');
    }
  } finally {
    await rm(root, { recursive: true });
  }
});
