import path from 'node:path';
import { closeSync, constants, existsSync, fstatSync, openSync, realpathSync, statSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { getSystemErrorName } from 'node:util';

const helper = fileURLToPath(new URL('./workspace-root-helper', import.meta.url));
export const closeWorkspace = Symbol('closeWorkspace');

function contains(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}

const operations = new Set([
  'readFile', 'stat', 'exists', 'readdir', 'readdirWithTypes',
  'writeFile', 'appendFile', 'mkdir', 'rm', 'rename',
]);

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
            'Build the pinned native rooted-filesystem helper on Linux/macOS amd64/arm64; ' +
            'unsupported hosts remain fail-closed.');
          error.code = 'ERR_EVALUATION_WORKSPACE_ISOLATION_REQUIRED';
          throw error;
        }
        return original.call(target, ...args);
      };
    },
  });
}

function invoke(fd, operation, args) {
  const [file, second, third] = args;
  const request = { protocol: 1, operation, path: file };
  if (operation === 'rename') request.dest = second;
  if (operation === 'writeFile' || operation === 'appendFile') {
    request.content = second;
    request.mode = third;
  }
  if (operation === 'mkdir') {
    request.recursive = second ?? false;
    request.mode = third;
  }
  if (operation === 'rm') {
    request.recursive = second ?? false;
    request.force = third ?? false;
  }
  if (operation === 'stat') request.node = process.execPath;
  const input = JSON.stringify(request);
  return new Promise((resolve, reject) => {
    const child = spawn(helper, [], { stdio: ['pipe', 'pipe', 'pipe', fd] });
    const output = [];
    const errors = [];
    let size = 0;
    let failure;
    const fail = error => {
      failure ??= error;
      child.kill();
    };
    const timer = setTimeout(() => fail(Object.assign(
      new Error(`Rooted filesystem operation '${operation}' timed out`),
      { code: 'ETIMEDOUT' })), 30_000);
    timer.unref();
    child.on('error', error => { failure ??= error; });
    child.stdin.on('error', error => { failure ??= error; });
    child.stdout.on('data', chunk => {
      size += chunk.length;
      if (size > 64 * 1024 * 1024) {
        fail(Object.assign(new Error('Rooted filesystem response exceeds 64 MiB'),
          { code: 'ERR_EVALUATION_WORKSPACE_RESPONSE_LIMIT' }));
      } else {
        output.push(chunk);
      }
    });
    child.stderr.on('data', chunk => {
      if (errors.length < 16) errors.push(chunk.toString());
    });
    child.on('close', (code, signal) => {
      clearTimeout(timer);
      if (failure) return reject(failure);
      if (code !== 0) return reject(new Error(
        `Rooted filesystem helper exited ${code ?? signal}: ${errors.join('')}`));
      let response;
      try {
        response = JSON.parse(Buffer.concat(output).toString('utf8'));
      } catch (cause) {
        return reject(new Error('Invalid rooted filesystem helper response', { cause }));
      }
      if (response?.protocol !== 1 || !Object.hasOwn(response, 'result')) {
        return reject(new Error('Unsupported rooted filesystem helper protocol'));
      }
      if (response.error) {
        const error = new Error(response.error.message);
        error.code = response.error.errno
          ? getSystemErrorName(-response.error.errno)
          : 'ERR_EVALUATION_WORKSPACE_ROOTED_IO';
        return reject(error);
      }
      resolve(response.result ?? undefined);
    });
    child.stdin.end(input);
  });
}

export function withWorkspaceAccess(provider, workingDirectory) {
  if (!['linux', 'darwin'].includes(process.platform) ||
      !['x64', 'arm64'].includes(process.arch) || !existsSync(helper)) {
    return withWorkspaceIsolationGuard(provider, workingDirectory);
  }
  const workspace = path.resolve(workingDirectory);
  const logs = path.resolve(provider.rootDir);
  if (workspace === path.parse(workspace).root) {
    throw new Error('The native workspace provider requires a bounded workspace, not a filesystem root');
  }
  // Capture the directory capability before the agent starts; helpers inherit
  // this descriptor rather than reopening the mutable workspace pathname.
  let fd = openSync(workspace, constants.O_RDONLY | constants.O_DIRECTORY | constants.O_NOFOLLOW);
  let canonical;
  try {
    canonical = realpathSync(workspace);
    const captured = fstatSync(fd, { bigint: true });
    const resolved = statSync(canonical, { bigint: true });
    if (captured.dev !== resolved.dev || captured.ino !== resolved.ino) {
      throw new Error('Workspace root changed while its directory capability was captured');
    }
  } catch (error) {
    closeSync(fd);
    throw error;
  }
  const roots = [...new Set([workspace, canonical])];
  const logRoots = [logs];
  if (contains(workspace, logs)) {
    logRoots.push(...roots.map(root => path.join(root, path.relative(workspace, logs))));
  }
  const workspaceRoot = file => {
    if (!path.isAbsolute(file)) return undefined;
    const absolute = path.resolve(file);
    if (logRoots.some(root => contains(root, absolute))) return undefined;
    return roots.find(root => contains(root, absolute));
  };
  return new Proxy(provider, {
    get(target, key) {
      if (key === closeWorkspace) return () => {
        if (fd !== undefined) {
          const closing = fd;
          fd = undefined;
          closeSync(closing);
        }
      };
      const original = Reflect.get(target, key);
      if (typeof original !== 'function') return original;
      if (!operations.has(key)) return original.bind(target);
      return async (...args) => {
        const paths = key === 'rename' ? args.slice(0, 2) : args.slice(0, 1);
        const workspacePaths = paths.map(workspaceRoot);
        if (!workspacePaths.some(Boolean)) return original.call(target, ...args);
        if (workspacePaths.some(value => !value)) {
          const error = new Error('Cannot rename between the workspace and session-log provider');
          error.code = 'ERR_EVALUATION_WORKSPACE_CROSS_ROOT_RENAME';
          throw error;
        }
        if (fd === undefined) {
          const error = new Error('Rooted workspace provider is closed');
          error.code = 'ERR_EVALUATION_WORKSPACE_PROVIDER_CLOSED';
          throw error;
        }
        const relative = paths.map((file, index) =>
          path.relative(workspacePaths[index], path.resolve(file)) || '.');
        return invoke(fd, key, [...relative, ...args.slice(paths.length)]);
      };
    },
  });
}
