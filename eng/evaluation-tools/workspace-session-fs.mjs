import {
  appendFile, lstat, mkdir, readFile, readdir, realpath, rename, rm, stat, writeFile,
} from 'node:fs/promises';
import path from 'node:path';

function contains(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}

const operations = {
  readFile: file => readFile(file, 'utf8'),
  stat: async file => {
    const info = await stat(file);
    return {
      isFile: info.isFile(), isDirectory: info.isDirectory(), size: info.size,
      mtime: info.mtime.toISOString(), birthtime: info.birthtime.toISOString(),
    };
  },
  exists: async file => { await stat(file); return true; },
  readdir: file => readdir(file),
  readdirWithTypes: async file => (await readdir(file, { withFileTypes: true })).map(entry => ({
    name: entry.name, type: entry.isDirectory() ? 'directory' : 'file',
  })),
  writeFile: async (file, content, mode) => {
    await mkdir(path.dirname(file), { recursive: true });
    await writeFile(file, content, { encoding: 'utf8', mode });
  },
  appendFile: async (file, content, mode) => {
    await mkdir(path.dirname(file), { recursive: true });
    await appendFile(file, content, { encoding: 'utf8', mode });
  },
  mkdir: async (file, recursive, mode) => { await mkdir(file, { recursive, mode }); },
  rm: (file, recursive, force) => rm(file, { recursive, force }),
  rename: async (source, destination) => {
    await mkdir(path.dirname(destination), { recursive: true });
    await rename(source, destination);
  },
};

const allowsMissing = new Set(['writeFile', 'appendFile', 'mkdir', 'rm']);

async function canonicalPath(file, allowMissing, followFinal = true) {
  if (!followFinal) {
    // Unlink and rename operate on entries, not a final symlink's target.
    const parent = await canonicalPath(path.dirname(file), allowMissing);
    const entry = path.join(parent, path.basename(file));
    if (!allowMissing) await lstat(entry);
    return entry;
  }
  const suffix = [];
  let candidate = file;
  while (true) {
    try {
      return path.join(await realpath(candidate), ...suffix);
    } catch (error) {
      if (!allowMissing || error.code !== 'ENOENT') throw error;
      try {
        const info = await lstat(candidate);
        if (info.isSymbolicLink()) {
          throw new Error(`Cannot resolve workspace symlink: ${candidate}`);
        }
      } catch (linkError) {
        if (linkError.code !== 'ENOENT') throw linkError;
      }
      const parent = path.dirname(candidate);
      if (parent === candidate) throw error;
      suffix.unshift(path.basename(candidate));
      candidate = parent;
    }
  }
}

// Native readers and patch tools share Vally's session-fs provider, but their
// workspace paths are separate from the session-log root.
export function withWorkspaceAccess(provider, workingDirectory) {
  const workspace = path.resolve(workingDirectory);
  let canonicalWorkspace;
  return new Proxy(provider, {
    get(target, key) {
      const original = Reflect.get(target, key);
      if (typeof original !== 'function') return original;
      if (!Object.hasOwn(operations, key)) return original.bind(target);
      return async (...args) => {
        const paths = key === 'rename' ? args.slice(0, 2) : args.slice(0, 1);
        if (!paths.every(file => path.isAbsolute(file))) return original.call(target, ...args);
        canonicalWorkspace ??= await realpath(workspace);
        const absolutePaths = paths.map(file => path.resolve(file));
        if (!absolutePaths.every(file =>
          contains(workspace, file) || contains(canonicalWorkspace, file))) {
          return original.call(target, ...args);
        }
        try {
          const canonicalPaths = await Promise.all(absolutePaths.map((file, index) =>
            canonicalPath(file, key === 'rename' ? index === 1 : allowsMissing.has(key),
              key !== 'rename' && key !== 'rm')));
          for (const file of canonicalPaths) {
            if (!contains(canonicalWorkspace, file)) {
              throw new Error(`Workspace filesystem path escapes root: ${file}`);
            }
            if ((key === 'rm' || key === 'rename') && file === canonicalWorkspace) {
              throw new Error(`Cannot remove or rename workspace root: ${file}`);
            }
          }
          return await operations[key](...canonicalPaths, ...args.slice(paths.length));
        } catch (error) {
          if (key === 'exists' && error.code === 'ENOENT') return false;
          throw error;
        }
      };
    },
  });
}
