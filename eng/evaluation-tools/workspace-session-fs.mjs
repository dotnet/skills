import path from 'node:path';

function contains(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}

const operations = new Set([
  'readFile', 'stat', 'exists', 'readdir', 'readdirWithTypes',
  'writeFile', 'appendFile', 'mkdir', 'rm', 'rename',
]);

// Reject workspace I/O without resolving or reopening mutable workspace paths.
export function withWorkspaceIsolationGuard(provider, workingDirectory) {
  const workspace = path.resolve(workingDirectory);
  const logs = path.resolve(provider.rootDir);
  return new Proxy(provider, {
    get(target, key) {
      const original = Reflect.get(target, key);
      if (typeof original !== 'function') return original;
      if (!operations.has(key)) return original.bind(target);
      return async (...args) => {
        const paths = key === 'rename' ? args.slice(0, 2) : args.slice(0, 1);
        if (paths.some(file => {
          if (!path.isAbsolute(file)) return false;
          const absolute = path.resolve(file);
          return contains(workspace, absolute) && !contains(logs, absolute);
        })) {
          const error = new Error(
            `Native workspace filesystem operation '${key}' is disabled: ` +
            'the local session-log provider cannot atomically confine workspace I/O. ' +
            'Use an upstream atomic workspace provider or a verified OS isolation boundary ' +
            'covering the Node provider and all workspace writers before enabling native workspace I/O.');
          error.code = 'ERR_EVALUATION_WORKSPACE_ISOLATION_REQUIRED';
          throw error;
        }
        return original.call(target, ...args);
      };
    },
  });
}
