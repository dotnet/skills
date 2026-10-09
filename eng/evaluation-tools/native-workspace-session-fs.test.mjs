import assert from 'node:assert/strict';
import { test } from 'node:test';
import { copyFile, mkdir, mkdtemp, readFile, readdir, rename, rm, stat, symlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createSessionFsAdapter } from '@github/copilot-sdk';
import { LocalSessionFsHandler } from './node_modules/@microsoft/vally/dist/executor/local-session-fs-handler.js';
import { closeWorkspace, withWorkspaceAccess } from './workspace-session-fs.mjs';

const supported = ['linux', 'darwin'].includes(process.platform) &&
  ['x64', 'arm64'].includes(process.arch);

test('a missing native helper rejects workspace extensions without changing log access', async () => {
  const root = await mkdtemp(path.join(process.cwd(), '.native-workspace-fs-test-'));
  try {
    const module = path.join(root, 'workspace-session-fs.mjs');
    await copyFile(new URL('./workspace-session-fs.mjs', import.meta.url), module);
    const isolated = await import(pathToFileURL(module).href);
    const workspace = path.join(root, 'workspace');
    const logs = path.join(root, 'logs');
    await Promise.all([workspace, logs].map(file => mkdir(file)));
    const provider = isolated.withWorkspaceAccess(new LocalSessionFsHandler(logs), workspace);
    assert.equal(provider[isolated.closeWorkspace], undefined);
    await assert.rejects(provider.readFile(path.join(workspace, 'file')),
      { code: 'ERR_EVALUATION_WORKSPACE_ISOLATION_REQUIRED' });
    await provider.writeFile('events.jsonl', 'log');
    assert.equal(await provider.readFile('events.jsonl'), 'log');
  } finally {
    await rm(root, { recursive: true });
  }
});

if (supported) {
  test('five concurrent workers retain independent workspace and log capabilities', async () => {
    const root = await mkdtemp(path.join(process.cwd(), '.native-workspace-fs-test-'));
    const providers = [];
    try {
      await Promise.all(Array.from({ length: 5 }, async (_, index) => {
        const workspace = path.join(root, `workspace-${index}`);
        const logs = path.join(root, `logs-${index}`);
        await Promise.all([workspace, logs].map(file => mkdir(file)));
        const provider = withWorkspaceAccess(new LocalSessionFsHandler(logs), workspace);
        providers.push(provider);
        assert.equal(typeof provider[closeWorkspace], 'function');
        const file = path.join(workspace, 'artifact.json');
        const content = JSON.stringify({ worker: index });
        await provider.writeFile(file, content);
        assert.equal(await provider.readFile(file), content);
        assert.equal((await provider.stat(file)).size, Buffer.byteLength(content));
        await provider.writeFile('events.jsonl', content);
        assert.equal(await provider.readFile('events.jsonl'), content);
        await provider.rename(file, path.join(workspace, 'final.json'));
        assert.equal(await provider.exists(file), false);
        assert.equal(await provider.readFile(path.join(workspace, 'final.json')), content);
      }));
    } finally {
      for (const provider of providers) provider[closeWorkspace]();
      await rm(root, { recursive: true });
    }
  });

  test('canonical ancestor aliases route to the captured workspace without reopening operation paths', async () => {
    const root = await mkdtemp(path.join(process.cwd(), '.native-workspace-fs-test-'));
    let provider;
    try {
      const physicalParent = path.join(root, 'physical');
      const workspace = path.join(physicalParent, 'workspace');
      const logs = path.join(root, 'logs');
      await Promise.all([workspace, logs].map(file => mkdir(file, { recursive: true })));
      const alias = path.join(root, 'alias');
      await symlink(physicalParent, alias);
      provider = withWorkspaceAccess(new LocalSessionFsHandler(logs), path.join(alias, 'workspace'));
      assert.equal(typeof provider[closeWorkspace], 'function');
      const physical = path.join(workspace, 'owned.txt');
      const declared = path.join(alias, 'workspace', 'owned.txt');
      await provider.writeFile(physical, 'canonical');
      assert.equal(await provider.readFile(declared), 'canonical');
      await provider.appendFile(declared, ' alias');
      assert.equal(await provider.readFile(physical), 'canonical alias');
    } finally {
      provider?.[closeWorkspace]?.();
      await rm(root, { recursive: true });
    }
  });

  test('native workspace operations use rooted capabilities and retain SDK metadata/log behavior', async () => {
    const root = await mkdtemp(path.join(process.cwd(), '.native-workspace-fs-test-'));
    let provider;
    try {
      const workspace = path.join(root, 'workspace');
      const logs = path.join(root, 'logs');
      await Promise.all([workspace, logs].map(file => mkdir(file)));
      provider = withWorkspaceAccess(new LocalSessionFsHandler(logs), workspace);
      assert.equal(typeof provider[closeWorkspace], 'function', 'build the pinned native helper first');
      const file = path.join(workspace, 'nested', 'file with "\nquotes.txt');
      await provider.writeFile(file, 'first', 0o640);
      await provider.appendFile(file, 'second');
      assert.equal(await provider.readFile(file), 'firstsecond');
      assert.equal(await provider.exists(file), true);
      assert.equal(await provider.exists(path.join(workspace, 'missing')), false);
      const info = await stat(file);
      assert.deepEqual(await provider.stat(file), {
        isFile: true, isDirectory: false, size: info.size,
        mtime: info.mtime.toISOString(), birthtime: info.birthtime.toISOString(),
      });
      assert.deepEqual(await provider.readdir(workspace), ['nested']);
      assert.deepEqual(await provider.readdirWithTypes(workspace), [{ name: 'nested', type: 'directory' }]);
      await provider.mkdir(path.join(workspace, 'created', 'child'), true, 0o700);
      const dest = path.join(workspace, 'renamed', 'file.txt');
      await provider.rename(file, dest);
      const adapter = createSessionFsAdapter(provider);
      assert.deepEqual(await adapter.readFile({ path: dest }), { content: 'firstsecond' });
      assert.equal(await adapter.writeFile({ path: dest, content: 'native patch' }), undefined);
      assert.equal(await readFile(dest, 'utf8'), 'native patch');
      await symlink('renamed/file.txt', path.join(workspace, 'inside-link'));
      assert.equal(await provider.readFile(path.join(workspace, 'inside-link')), 'native patch');
      await provider.writeFile('events.jsonl', 'log');
      assert.equal(await provider.readFile(path.join(logs, 'events.jsonl')), 'log');
      for (const args of [['events.jsonl', dest], [dest, 'events.jsonl']]) {
        await assert.rejects(provider.rename(...args), { code: 'ERR_EVALUATION_WORKSPACE_CROSS_ROOT_RENAME' });
      }
      await provider.rm(dest, false, false);
      await assert.rejects(provider.rm(dest, false, false), { code: 'ENOENT' });
      await provider.rm(dest, false, true);
      await provider.rm(path.join(workspace, 'created'), true, false);
      await assert.rejects(provider.rm(workspace, true, true), /workspace root/);
      provider[closeWorkspace]();
      provider[closeWorkspace]();
      await assert.rejects(provider.readFile(dest), { code: 'ERR_EVALUATION_WORKSPACE_PROVIDER_CLOSED' });
      assert.equal(await provider.readFile('events.jsonl'), 'log');
    } finally {
      provider?.[closeWorkspace]?.();
      await rm(root, { recursive: true });
    }
  });

  test('in-flight and subsequent parent swaps never access outside sentinels for all ten operations', async () => {
    const root = await mkdtemp(path.join(process.cwd(), '.native-workspace-fs-test-'));
    let provider;
    try {
      const workspace = path.join(root, 'workspace');
      const logs = path.join(root, 'logs');
      const outside = path.join(root, 'outside');
      const parent = path.join(workspace, 'parent');
      const saved = path.join(workspace, 'saved');
      await Promise.all([parent, logs, outside].map(file => mkdir(file, { recursive: true })));
      const file = path.join(parent, 'owned.txt');
      const outsideFile = path.join(outside, 'owned.txt');
      await writeFile(path.join(outside, 'outside-marker'), 'outside');
      provider = withWorkspaceAccess(new LocalSessionFsHandler(logs), workspace);
      assert.equal(typeof provider[closeWorkspace], 'function');
      for (const [operation, args] of [
        ['readFile', [file]], ['stat', [file]], ['exists', [file]],
        ['readdir', [parent]], ['readdirWithTypes', [parent]],
        ['writeFile', [file, 'changed']], ['appendFile', [file, 'changed']],
        ['mkdir', [path.join(parent, 'new'), true]],
        ['rm', [file, false, false]], ['rename', [file, path.join(parent, 'renamed')]],
      ]) {
        await writeFile(file, 'inside');
        await writeFile(outsideFile, 'outside');
        const pending = provider[operation](...args).then(value => ({ value }), error => ({ error }));
        await rename(parent, saved);
        await symlink(outside, parent);
        const result = await pending;
        if (operation === 'readFile' && !result.error) assert.equal(result.value, 'inside');
        if (operation === 'stat' && !result.error) assert.equal(result.value.size, 6);
        if (operation === 'exists' && !result.error) assert.equal(result.value, true);
        if (operation === 'readdir' && !result.error) assert.deepEqual(result.value, ['owned.txt']);
        if (operation === 'readdirWithTypes' && !result.error) {
          assert.deepEqual(result.value, [{ name: 'owned.txt', type: 'file' }]);
        }
        await assert.rejects(provider[operation](...args), /escapes|outside|parent/i);
        assert.equal(await readFile(outsideFile, 'utf8'), 'outside');
        assert.deepEqual((await readdir(outside)).sort(), ['outside-marker', 'owned.txt']);
        assert.deepEqual(await provider.readdir(logs), []);
        await rm(parent);
        await rename(saved, parent);
        await provider.rm(path.join(parent, 'new'), true, true);
        await provider.rm(path.join(parent, 'renamed'), false, true);
      }
    } finally {
      provider?.[closeWorkspace]?.();
      await rm(root, { recursive: true });
    }
  });

  test('replacement of the entire workspace pathname does not change the inherited capability', async () => {
    const root = await mkdtemp(path.join(process.cwd(), '.native-workspace-fs-test-'));
    let provider;
    try {
      const workspace = path.join(root, 'workspace');
      const logs = path.join(root, 'logs');
      const outside = path.join(root, 'outside');
      await Promise.all([workspace, logs, outside].map(file => mkdir(file)));
      await writeFile(path.join(workspace, 'owned.txt'), 'inside');
      await writeFile(path.join(outside, 'owned.txt'), 'outside');
      provider = withWorkspaceAccess(new LocalSessionFsHandler(logs), workspace);
      assert.equal(typeof provider[closeWorkspace], 'function');
      await rename(workspace, path.join(root, 'saved'));
      await symlink(outside, workspace);
      const file = path.join(workspace, 'owned.txt');
      assert.equal(await provider.readFile(file), 'inside');
      await provider.writeFile(file, 'rooted');
      assert.equal(await readFile(path.join(root, 'saved', 'owned.txt'), 'utf8'), 'rooted');
      assert.equal(await readFile(path.join(outside, 'owned.txt'), 'utf8'), 'outside');
    } finally {
      provider?.[closeWorkspace]?.();
      await rm(root, { recursive: true });
    }
  });
} else {
  test('unsupported native helper hosts remain fail-closed', async () => {
    const root = await mkdtemp(path.join(process.cwd(), '.native-workspace-fs-test-'));
    try {
      const workspace = path.join(root, 'workspace');
      const logs = path.join(root, 'logs');
      await Promise.all([workspace, logs].map(file => mkdir(file)));
      const provider = withWorkspaceAccess(new LocalSessionFsHandler(logs), workspace);
      await assert.rejects(provider.readFile(path.join(workspace, 'file')),
        { code: 'ERR_EVALUATION_WORKSPACE_ISOLATION_REQUIRED' });
    } finally {
      await rm(root, { recursive: true });
    }
  });
}
