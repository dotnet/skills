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

test('all executor arms receive bounded lifecycle guidance, but judges do not', async () => {
  const { client, entered, release } = clientFixture();
  const starting = client.start();
  await entered.promise;
  release.resolve();
  await starting;
  const requests = [];
  client.connection.sendRequest = async (method, params) => {
    requests.push(params);
    return { sessionId: params.sessionId };
  };
  for (const skills of [[], ['isolated'], ['one', 'two']]) {
    const config = {
      sessionId: 'executor',
      skillDirectories: skills,
      systemMessage: { mode: 'append', content: 'Keep existing guidance.' },
    };
    await client.createSession(config);
    assert.equal(config.systemMessage.content, 'Keep existing guidance.');
    const sent = requests.at(-1);
    assert.match(sent.systemMessage.content, /^Keep existing guidance\./);
    assert.match(sent.systemMessage.content, /stop any persistent watchers/);
    assert.equal(sent.systemMessage.mode, 'append');
  }
  await client.createSession({ sessionId: 'judge', systemMessage: { mode: 'replace', content: 'Judge only.' } });
  assert.deepEqual(requests.at(-1).systemMessage, { mode: 'replace', content: 'Judge only.' });
});

test('executor guidance preserves session configuration on create and resume', async () => {
  const { client, entered, release } = clientFixture();
  const starting = client.start();
  await entered.promise;
  release.resolve();
  await starting;
  let sent;
  client.connection.sendRequest = async (method, params) => {
    sent = params;
    return { sessionId: params.sessionId };
  };
  for (const method of ['createSession', 'resumeSession']) {
    for (const systemMessage of [
      undefined,
      { mode: 'replace', content: 'Replace only when requested.' },
      { mode: 'customize', content: 'Extra guidance.', sections: { tone: { action: 'remove' } } },
    ]) {
      const config = { sessionId: 'executor', skillDirectories: [], systemMessage };
      if (method === 'createSession') await client.createSession(config);
      else await client.resumeSession('executor', config);
      assert.equal(sent.systemMessage.mode, systemMessage?.mode ?? 'append');
      assert.match(sent.systemMessage.content, /bounded evaluation session/);
      if (systemMessage?.content) assert.ok(sent.systemMessage.content.startsWith(systemMessage.content));
      if (systemMessage?.sections) assert.deepEqual(sent.systemMessage.sections, systemMessage.sections);
      assert.equal(config.systemMessage, systemMessage);
    }
  }
});

async function executorFixture() {
  const { client, entered, release } = clientFixture();
  const starting = client.start();
  await entered.promise;
  release.resolve();
  await starting;
  const requests = [];
  let taskList = async () => ({ tasks: [] });
  let send = async () => ({ messageId: 'message' });
  let drain = async () => ({});
  let activity = async () => ({ hasActiveWork: false, abortable: false });
  client.connection.sendRequest = async (method, params) => {
    requests.push({ method, params });
    if (method === 'session.tasks.list') return taskList();
    if (method === 'session.tasks.waitForPending') return drain();
    if (method === 'session.metadata.activity') return activity();
    if (method === 'session.send') return send(params);
    return { sessionId: params.sessionId, messageId: 'message' };
  };
  const session = await client.createSession({ sessionId: 'bounded', skillDirectories: [] });
  return {
    session, requests,
    setTasks: handler => { taskList = handler; },
    setSend: handler => { send = handler; },
    setDrain: handler => { drain = handler; },
    setActivity: handler => { activity = handler; },
    emit: (type, data = {}, extra = {}) => session._dispatchEvent({ type, data, ...extra }),
  };
}

const attachedShell = { id: 'shell-7', type: 'shell', status: 'running', attachmentMode: 'attached' };

test('main-agent idle requests shell cleanup once, but only real session idle completes the turn', async () => {
  const { session, requests, setTasks, emit } = await executorFixture();
  setTasks(async () => ({ tasks: [attachedShell] }));
  let settled = false;
  const waiting = session.sendAndWait('Original task', 1000).then(result => { settled = true; return result; });
  emit('assistant.message', { content: 'Original answer before cleanup.' });
  emit('assistant.idle');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(settled, false);
  const messages = requests.filter(request => request.method === 'session.send');
  assert.equal(messages.length, 2);
  assert.equal(messages[1].params.mode, 'immediate');
  assert.match(messages[1].params.prompt, /Pending shell IDs: shell-7$/);
  assert.match(messages[1].params.prompt, /complete final answer to the original task/);
  emit('assistant.idle');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(requests.filter(request => request.method === 'session.send').length, 2);
  emit('assistant.message', { content: 'Complete original answer with final process status.' });
  emit('session.idle', { mode: 'interactive' });
  assert.equal((await waiting).data.content, 'Complete original answer with final process status.');
  assert.equal(settled, true);
});

test('idle cleanup ignores completed shells, detached work, background agents, subagents, and aborts', async () => {
  const { session, requests, setTasks, emit } = await executorFixture();
  const waiting = session.sendAndWait('Original task', 1000);
  for (const extra of [{ agentId: 'child' }, { data: { aborted: true } }]) {
    emit('assistant.idle', extra.data ?? {}, extra.agentId ? extra : {});
  }
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(requests.some(request => request.method === 'session.tasks.list'), false);
  for (const task of [
    { type: 'shell', status: 'completed', attachmentMode: 'attached' },
    { type: 'shell', status: 'running', attachmentMode: 'detached' },
    { type: 'agent', status: 'running' },
    { type: 'agent', status: 'idle' },
  ]) {
    setTasks(async () => ({ tasks: [task, ...(task.type === 'agent' ? [attachedShell] : [])] }));
    emit('assistant.idle');
    await new Promise(resolve => setImmediate(resolve));
  }
  assert.equal(requests.filter(request => request.method === 'session.send').length, 1);
  emit('session.idle', { mode: 'interactive' });
  await waiting;
});

test('late task inspection cannot send a cleanup message after session completion', async () => {
  const { session, requests, setTasks, emit } = await executorFixture();
  const tasks = deferred();
  setTasks(() => tasks.promise);
  const waiting = session.sendAndWait('Original task', 1000);
  emit('assistant.idle');
  emit('session.idle', { mode: 'interactive' });
  await waiting;
  tasks.resolve({ tasks: [attachedShell] });
  await new Promise(resolve => setImmediate(resolve));
  emit('assistant.idle');
  assert.equal(requests.filter(request => request.method === 'session.send').length, 1);
});

test('optional task inspection failures are diagnostic and retain the original idle wait', async t => {
  const { session, setTasks, emit } = await executorFixture();
  const failure = new Error('task inspection failed');
  const warning = t.mock.method(console, 'warn', () => {});
  setTasks(async () => { throw failure; });
  const waiting = session.sendAndWait('Original task', 1000);
  emit('assistant.idle');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(warning.mock.calls.length, 1);
  assert.equal(warning.mock.calls[0].arguments[1], failure);
  emit('assistant.message', { content: 'Complete original answer.' });
  emit('session.idle', { mode: 'interactive' });
  assert.equal((await waiting).data.content, 'Complete original answer.');
});

test('an original idle racing cleanup admission waits for the cleanup turn itself', async () => {
  const { session, requests, setTasks, setSend, setActivity, emit } = await executorFixture();
  const admitted = deferred();
  let hasActiveWork = true;
  setTasks(async () => ({ tasks: [attachedShell] }));
  setSend(async params => params.prompt === 'Original task' ? { messageId: 'original' } : admitted.promise);
  setActivity(async () => ({ hasActiveWork, abortable: hasActiveWork }));
  let settled = false;
  const waiting = session.sendAndWait('Original task', 1000).then(result => { settled = true; return result; });
  emit('assistant.idle');
  await new Promise(resolve => setImmediate(resolve));
  emit('assistant.message', { content: 'Answer before cleanup.' });
  emit('session.idle', { mode: 'interactive' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(settled, false);
  admitted.resolve({ messageId: 'cleanup' });
  await new Promise(resolve => setImmediate(resolve));
  emit('session.idle', { mode: 'interactive' }, { agentId: 'child' });
  emit('session.idle', { mode: 'autopilot' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(requests.some(request => request.method === 'session.tasks.waitForPending'), false);
  assert.equal(settled, false);
  emit('assistant.message', { content: 'Full original answer and completed cleanup.' });
  hasActiveWork = false;
  emit('session.idle', { mode: 'interactive' });
  assert.equal((await waiting).data.content, 'Full original answer and completed cleanup.');
});

test('cleanup send errors remain executor errors', async () => {
  const { session, setTasks, setSend, emit } = await executorFixture();
  const failure = new Error('cleanup send failed');
  setTasks(async () => ({ tasks: [attachedShell] }));
  setSend(async params => {
    if (params.prompt !== 'Original task') throw failure;
    return { messageId: 'original' };
  });
  const waiting = session.sendAndWait('Original task', 1000);
  const rejected = assert.rejects(waiting, error => error === failure);
  emit('assistant.idle');
  await rejected;
  emit('session.idle', { mode: 'interactive' });
});

test('cleanup does not extend the original session idle timeout', async () => {
  const { session, setTasks, emit } = await executorFixture();
  setTasks(async () => ({ tasks: [attachedShell] }));
  const waiting = session.sendAndWait('Original task', 30);
  const rejected = assert.rejects(waiting, /Timeout after 30ms waiting for session.idle/);
  emit('assistant.idle');
  await rejected;
});

test('a drain timeout cannot turn pending cleanup into a completed measurement', async () => {
  const { session, setTasks, setActivity, emit } = await executorFixture();
  setTasks(async () => ({ tasks: [attachedShell] }));
  setActivity(async () => ({ hasActiveWork: true, abortable: true }));
  const waiting = session.sendAndWait('Original task', 30);
  const rejected = assert.rejects(waiting, /Timeout after 30ms waiting for session.idle/);
  emit('assistant.idle');
  await new Promise(resolve => setImmediate(resolve));
  emit('session.idle', { mode: 'interactive' });
  await rejected;
});

test('the original deadline also bounds cleanup drain after an early SDK idle', async () => {
  const { session, setTasks, setDrain, emit } = await executorFixture();
  setTasks(async () => ({ tasks: [attachedShell] }));
  setDrain(() => new Promise(() => {}));
  const waiting = session.sendAndWait('Original task', 30);
  const rejected = assert.rejects(waiting, /Timeout after 30ms waiting for session.idle/);
  emit('assistant.idle');
  await new Promise(resolve => setImmediate(resolve));
  emit('session.idle', { mode: 'interactive' });
  await rejected;
});

test('cleanup drain errors remain executor errors', async () => {
  const { session, setTasks, setDrain, emit } = await executorFixture();
  const failure = new Error('task drain failed');
  setTasks(async () => ({ tasks: [attachedShell] }));
  setDrain(async () => { throw failure; });
  const waiting = session.sendAndWait('Original task', 1000);
  const rejected = assert.rejects(waiting, error => error === failure);
  emit('assistant.idle');
  await new Promise(resolve => setImmediate(resolve));
  emit('session.idle', { mode: 'interactive' });
  await rejected;
});

test('work still active after an idle is allowed to finish within the same deadline', async () => {
  const { session, setTasks, setActivity, emit } = await executorFixture();
  let hasActiveWork = true;
  setTasks(async () => ({ tasks: [attachedShell] }));
  setActivity(async () => ({ hasActiveWork, abortable: hasActiveWork }));
  let settled = false;
  const waiting = session.sendAndWait('Original task', 1000).then(result => { settled = true; return result; });
  emit('assistant.idle');
  await new Promise(resolve => setImmediate(resolve));
  emit('session.idle', { mode: 'interactive' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(settled, false);
  hasActiveWork = false;
  emit('assistant.message', { content: 'Full answer after all work finished.' });
  emit('session.idle', { mode: 'interactive' });
  assert.equal((await waiting).data.content, 'Full answer after all work finished.');
});
