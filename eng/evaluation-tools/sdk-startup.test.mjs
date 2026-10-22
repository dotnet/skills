import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CopilotClient } from '@github/copilot-sdk';
import './sdk-startup.mjs';

function deferred() {
  let resolve;
  const promise = new Promise(r => { resolve = r; });
  return { promise, resolve };
}

// Use the real SDK startup/session methods, but no runtime process or model calls.
function clientFixture() {
  const client = new CopilotClient({
    useLoggedInUser: false,
    connection: { kind: 'stdio', path: process.execPath },
  });
  const entered = deferred();
  const release = deferred();
  const calls = [];
  client.sessionFsConfig = {
    initialCwd: '/work', sessionStatePath: '/sessions', conventions: 'posix',
  };
  client.startCLIServer = async () => { calls.push('spawn'); };
  client.connectToServer = async () => {
    client.connection = {
      sendRequest: async (method, params) => {
        calls.push(method);
        if (method === 'sessionFs.setProvider') {
          entered.resolve();
          await release.promise;
        }
        if (method === 'session.create' || method === 'session.resume') {
          assert.equal(client.state, 'connected', 'session started before provider was ready');
          return { sessionId: params.sessionId };
        }
        return {};
      },
    };
  };
  client.verifyProtocolVersion = async () => {};
  client.setupSessionFs = () => {};
  client.forceStop = async () => { client.connection = null; };
  return { client, calls, entered, release };
}

test('concurrent starts share one runtime and provider initialization', async () => {
  const { client, calls, entered, release } = clientFixture();
  const starts = [client.start(), client.start(), client.start()];
  await entered.promise;
  release.resolve();
  await Promise.all(starts);
  assert.equal(calls.filter(c => c === 'spawn').length, 1);
  assert.equal(calls.filter(c => c === 'sessionFs.setProvider').length, 1);
});

for (const method of ['createSession', 'resumeSession']) {
  test(`${method} waits for provider initialization even after transport connects`, async () => {
    const { client, calls, entered, release } = clientFixture();
    const starting = client.start();
    await entered.promise;
    const session = method === 'createSession'
      ? client.createSession({ sessionId: 'trial' })
      : client.resumeSession('trial', {});
    // Attach a handler now so the unfixed SDK's early rejection is not unhandled.
    const settled = session.then(value => ({ value }), error => ({ error }));
    await new Promise(resolve => setImmediate(resolve));
    const earlyRequests = calls.filter(c => c === 'session.create' || c === 'session.resume');
    release.resolve();
    await starting;
    const result = await settled;
    assert.deepEqual(earlyRequests, []);
    assert.ifError(result.error);
    assert.equal(result.value.sessionId, 'trial');
  });
}

test('failed startup rejects every waiter and permits a later retry', async () => {
  const { client, entered, release, calls } = clientFixture();
  const failure = new Error('startup failed');
  client.verifyProtocolVersion = async () => {
    entered.resolve();
    await release.promise;
    throw failure;
  };
  const starting = [client.start(), client.start()];
  const settled = Promise.allSettled(starting);
  await entered.promise;
  release.resolve();
  assert.ok((await settled).every(r => r.status === 'rejected' && r.reason === failure));
  client.verifyProtocolVersion = async () => {};
  await client.start();
  assert.equal(client.state, 'connected');
  assert.equal(calls.filter(c => c === 'spawn').length, 2);
});

test('independent clients do not share a startup lock', async () => {
  const first = clientFixture();
  const second = clientFixture();
  const starts = [first.client.start(), second.client.start()];
  await Promise.all([first.entered.promise, second.entered.promise]);
  second.release.resolve();
  await starts[1];
  assert.equal(first.client.state, 'connecting');
  first.release.resolve();
  await starts[0];
});

test('ready clients still create sessions concurrently', async () => {
  const { client, entered, release, calls } = clientFixture();
  const starting = client.start();
  await entered.promise;
  release.resolve();
  await starting;
  const bothEntered = deferred();
  const finish = deferred();
  let count = 0;
  client.connection.sendRequest = async (method, params) => {
    assert.equal(method, 'session.create');
    if (++count === 2) bothEntered.resolve();
    await finish.promise;
    return { sessionId: params.sessionId };
  };
  const sessions = [
    client.createSession({ sessionId: 'first' }),
    client.createSession({ sessionId: 'second' }),
  ];
  await bothEntered.promise;
  finish.resolve();
  await Promise.all(sessions);
  assert.equal(calls.filter(c => c === 'spawn').length, 1);
});
