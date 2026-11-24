import assert from 'node:assert/strict';
import { test } from 'node:test';
import { toolSchemaIdentity } from './runtime-identity.mjs';

const view = {
  name: 'view',
  description: 'Read a file',
  parameters: { type: 'object', properties: { path: { type: 'string' } }, required: ['path'] },
};
const edit = {
  name: 'edit',
  description: 'Edit a file',
  parameters: { type: 'object', properties: { path: { type: 'string' }, text: { type: 'string' } } },
};

test('tool schema identity ignores list and key order', () => {
  const reordered = {
    parameters: { required: ['path'], properties: { path: { type: 'string' } }, type: 'object' },
    description: 'Read a file',
    name: 'view',
  };

  const identity = toolSchemaIdentity([view, edit]);

  assert.deepEqual(toolSchemaIdentity([edit, reordered]), identity);
  assert.equal(identity.toolCount, 2);
  assert.deepEqual(identity.toolNames, ['edit', 'view']);
  assert.match(identity.sha256, /^[0-9a-f]{64}$/);
});

test('tool schema identity changes when a model-visible field changes', () => {
  const baseline = toolSchemaIdentity([view, edit]).sha256;

  assert.notEqual(toolSchemaIdentity([{ ...view, description: 'Read a file or directory' }, edit]).sha256, baseline);
  assert.notEqual(toolSchemaIdentity([{ ...view, instructions: 'Prefer view over cat.' }, edit]).sha256, baseline);
  assert.notEqual(toolSchemaIdentity([view]).sha256, baseline);
});
