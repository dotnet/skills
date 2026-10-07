import { readFile, readdir, realpath, stat } from 'node:fs/promises';
import path from 'node:path';

function contains(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}

const readers = {
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
};

// Session logs and workspace inputs are separate roots in Vally 0.14. Native
// readers also use the session-fs provider; allow only reads of that trial's inputs.
export function withWorkspaceReads(provider, workingDirectory) {
  const workspace = path.resolve(workingDirectory);
  let canonicalWorkspace;
  return new Proxy(provider, {
    get(target, key) {
      const original = Reflect.get(target, key);
      if (typeof original !== 'function') return original;
      if (!Object.hasOwn(readers, key)) return original.bind(target);
      return async (file, ...args) => {
        if (!path.isAbsolute(file)) return original.call(target, file, ...args);
        canonicalWorkspace ??= await realpath(workspace);
        const absolute = path.resolve(file);
        if (!contains(workspace, absolute) && !contains(canonicalWorkspace, absolute)) {
          return original.call(target, file, ...args);
        }
        try {
          const canonicalFile = await realpath(absolute);
          if (!contains(canonicalWorkspace, canonicalFile)) {
            throw new Error(`Workspace read escapes root: ${file}`);
          }
          return await readers[key](canonicalFile);
        } catch (error) {
          if (key === 'exists' && error.code === 'ENOENT') return false;
          throw error;
        }
      };
    },
  });
}
