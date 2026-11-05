import { readFileSync } from 'node:fs';
import { CopilotClient } from '@github/copilot-sdk';

const sdkPackage = new URL('../package.json', import.meta.resolve('@github/copilot-sdk'));
const { version } = JSON.parse(readFileSync(sdkPackage, 'utf8'));
if (!['1.0.11', '1.0.13', '1.0.14'].includes(version)) {
  throw new Error(`Reassess the evaluation SDK startup compatibility layer for SDK ${version}`);
}

// SDK 1.0.11, 1.0.13, and 1.0.14 can start multiple transports and expose a connection before
// sessionFs.setProvider finishes. Remove after an SDK upgrade covers both races.
// Upstream startup tracking: https://github.com/github/copilot-sdk/pull/2585
const starts = new WeakMap();
const boundedTaskInstructions = [
  'This is a bounded evaluation session.',
  'Before your final answer, wait for finite background work to finish and stop',
  'any persistent watchers, development servers, or other processes you started.',
  'Do not leave attached background work running after you finish the task.',
].join(' ');

function boundSessionLifecycle(session) {
  const sendAndWait = session.sendAndWait;
  session.sendAndWait = async function (...args) {
    let active = true;
    let checking = false;
    let reminded = false;
    let cleanupSend;
    let cleanupIdle;
    let lastAssistantMessage;
    let resolveIdle;
    const newIdleWait = () => new Promise(resolve => { resolveIdle = resolve; });
    let nextIdle = newIdleWait();
    let rejectCleanup;
    const cleanupFailure = new Promise((_, reject) => { rejectCleanup = reject; });
    const timeout = args[1] ?? 60_000;
    const deadline = setTimeout(() => rejectCleanup(
      new Error(`Timeout after ${timeout}ms waiting for session.idle`)), timeout);
    const unsubscribe = this.on(event => {
      if (event.type === 'assistant.message' && !event.agentId) lastAssistantMessage = event;
      if (event.type === 'session.error') rejectCleanup(new Error(event.data.message));
      if (event.type === 'session.idle' && !event.agentId && event.data.mode !== 'autopilot') {
        resolveIdle();
        nextIdle = newIdleWait();
      }
      if (event.type !== 'assistant.idle') return;
      if (!active || checking || reminded || event.agentId || event.data.aborted) return;
      checking = true;
      void this.rpc.tasks.list().then(async ({ tasks }) => {
        if (!active || tasks.some(task =>
          task.type === 'agent' && ['running', 'idle'].includes(task.status))) return;
        const shells = tasks.filter(task =>
          task.type === 'shell' && task.status === 'running' && task.attachmentMode === 'attached');
        if (shells.length === 0) return;
        reminded = true;
        cleanupSend = this.send({
          mode: 'immediate',
          prompt: 'This bounded evaluation cannot finish while attached shell tasks are running. '
            + 'Wait for finite work to finish; stop persistent processes using their shell IDs. '
            + 'Then give a complete final answer to the original task, including the final process status; '
            + 'do not answer only with a cleanup confirmation. Pending shell IDs: '
            + shells.map(task => task.id).join(', '),
        }).then(() => { cleanupIdle = nextIdle; });
        await cleanupSend.catch(error => { rejectCleanup(error); });
      }).catch(error => {
        if (active) console.warn('Evaluation lifecycle task inspection failed; retaining the SDK idle wait:', error);
      }).finally(() => { checking = false; });
    });
    try {
      const completion = (async () => {
        const result = await sendAndWait.apply(this, args);
        active = false;
        if (!cleanupSend) return result;
        // The original idle can race cleanup admission. Drain the admitted
        // follow-up before Vally captures the workspace or starts another turn.
        await cleanupSend;
        await cleanupIdle;
        for (;;) {
          const idle = nextIdle;
          await this.rpc.tasks.waitForPending();
          const activity = await this.rpc.metadata.activity();
          if (activity.hasActiveWork === false) break;
          await idle;
        }
        return lastAssistantMessage ?? result;
      })();
      return await Promise.race([completion, cleanupFailure]);
    } finally {
      active = false;
      clearTimeout(deadline);
      unsubscribe();
    }
  };
}

const originalStart = CopilotClient.prototype.start;
CopilotClient.prototype.start = function (...args) {
  let starting = starts.get(this);
  if (!starting) {
    starting = Promise.resolve()
      .then(() => originalStart.apply(this, args))
      .finally(() => starts.delete(this));
    starts.set(this, starting);
  }
  return starting;
};

for (const method of ['createSession', 'resumeSession']) {
  const original = CopilotClient.prototype[method];
  CopilotClient.prototype[method] = async function (...args) {
    await this.start();
    const configIndex = method === 'createSession' ? 0 : 1;
    const config = args[configIndex];
    // Vally executor sessions declare skillDirectories, including [] for baseline.
    // Judge sessions do not; leave their prompts unchanged.
    if (Array.isArray(config?.skillDirectories)) {
      args[configIndex] = {
        ...config,
        systemMessage: {
          ...config.systemMessage,
          mode: config.systemMessage?.mode ?? 'append',
          content: [config.systemMessage?.content, boundedTaskInstructions].filter(Boolean).join('\n\n'),
        },
      };
    }
    const session = await original.apply(this, args);
    if (Array.isArray(config?.skillDirectories)) boundSessionLifecycle(session);
    return session;
  };
}
