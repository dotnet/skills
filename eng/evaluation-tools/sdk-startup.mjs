import { readFileSync } from 'node:fs';
import { CopilotClient } from '@github/copilot-sdk';
import { LocalSessionFsHandler } from './node_modules/@microsoft/vally/dist/executor/local-session-fs-handler.js';
import { withWorkspaceAccess } from './workspace-session-fs.mjs';

const sdkPackage = new URL('../package.json', import.meta.resolve('@github/copilot-sdk'));
const { version } = JSON.parse(readFileSync(sdkPackage, 'utf8'));
if (!['1.0.11', '1.0.13'].includes(version)) {
  throw new Error(`Reassess the evaluation SDK startup compatibility layer for SDK ${version}`);
}
const { version: vallyVersion } = JSON.parse(readFileSync(
  new URL('./node_modules/@microsoft/vally/package.json', import.meta.url), 'utf8'));
if (vallyVersion !== '0.14.0') {
  throw new Error(`Reassess the evaluation workspace-filesystem compatibility layer for Vally ${vallyVersion}`);
}

// SDK 1.0.11 and 1.0.13 can start multiple transports and expose a connection before
// sessionFs.setProvider finishes. Remove after an SDK upgrade covers both races.
// Upstream startup tracking: https://github.com/github/copilot-sdk/pull/2585
const starts = new WeakMap();
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
    if (method === 'createSession' && args[0]?.createSessionFsProvider && args[0].workingDirectory) {
      const config = args[0];
      args[0] = {
        ...config,
        createSessionFsProvider: (...factoryArgs) => {
          const provider = config.createSessionFsProvider(...factoryArgs);
          return provider instanceof LocalSessionFsHandler
            ? withWorkspaceAccess(provider, config.workingDirectory)
            : provider;
        },
      };
    }
    return original.apply(this, args);
  };
}
