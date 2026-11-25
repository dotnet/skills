import { readFileSync } from 'node:fs';
import { CopilotClient } from '@github/copilot-sdk';

const sdkPackage = new URL('../package.json', import.meta.resolve('@github/copilot-sdk'));
const { version } = JSON.parse(readFileSync(sdkPackage, 'utf8'));
if (!['1.0.11', '1.0.13', '1.0.14', '1.0.15', '1.0.26', '1.0.29'].includes(version)) {
  throw new Error(`Reassess the evaluation SDK startup compatibility layer for SDK ${version}`);
}
const { version: vallyVersion } = JSON.parse(readFileSync(
  new URL('./node_modules/@microsoft/vally/package.json', import.meta.url), 'utf8'));
if (vallyVersion !== '0.21.0') {
  throw new Error(`Reassess the evaluation native-session persistence contract for Vally ${vallyVersion}`);
}

// SDK 1.0.11, 1.0.13, and 1.0.14 can start multiple transports. Supported versions
// through 1.0.26 also require session creation to wait for sessionFs.setProvider.
// SDK 1.0.29 fixes both races; only the independent provider policy remains.
// Upstream startup tracking: https://github.com/github/copilot-sdk/pull/2585
if (version !== '1.0.29') {
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

}

for (const method of ['createSession', 'resumeSession']) {
  const original = CopilotClient.prototype[method];
  CopilotClient.prototype[method] = async function (...args) {
    const config = args[method === 'createSession' ? 0 : 1];
    if (config?.createSessionFsProvider) {
      throw new Error(
        'Evaluation session filesystem providers are unsupported: Vally 0.21 uses native SDK ' +
        'persistence and exports session history without a session-log filesystem provider. ' +
        'Reassess workspace isolation before enabling a custom provider.');
    }
    if (version !== '1.0.29') await this.start();
    return original.apply(this, args);
  };
}
