const fs = require('node:fs');
const path = require('node:path');

function verifyReplay(directory, repositoryName) {
  if (!directory || !repositoryName) {
    throw new Error('A replay directory and repository name are required.');
  }

  const root = fs.realpathSync(directory);
  const revision = fs.readFileSync(path.join(root, '.agentviz-commit'), 'utf8').trim();
  if (!/^[0-9a-f]{40}$/i.test(revision)) {
    throw new Error('The replay viewer has no valid revision marker.');
  }

  const html = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
  const scripts = [...html.matchAll(/<script\b[^>]*?\bsrc\s*=\s*(["'])(.*?)\1[^>]*>/gi)];
  const stylesheets = [...html.matchAll(/<link\b[^>]*?\bhref\s*=\s*(["'])(.*?)\1[^>]*>/gi)]
    .filter(match => /\brel\s*=\s*(["'])[^"']*\b(?:stylesheet|modulepreload)\b[^"']*\1/i.test(match[0]));
  const baseUrl = new URL(`https://dashboard.invalid/${encodeURIComponent(repositoryName)}/replay/index.html`);
  const replayPath = baseUrl.pathname.slice(0, -'index.html'.length);
  let localScripts = 0;

  for (const match of [...scripts, ...stylesheets]) {
    const url = new URL(match[2], baseUrl);
    if (url.origin !== baseUrl.origin) continue;
    if (!url.pathname.startsWith(replayPath)) {
      throw new Error(`Replay asset is outside the viewer: ${match[2]}`);
    }

    const asset = path.resolve(root, decodeURIComponent(url.pathname.slice(replayPath.length)));
    const realAsset = fs.realpathSync(asset);
    const relative = path.relative(root, realAsset);
    if (!relative || relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
      throw new Error(`Replay asset is outside the viewer: ${match[2]}`);
    }
    const stat = fs.statSync(realAsset);
    if (!stat.isFile() || stat.size === 0) {
      throw new Error(`Replay asset is empty or not a file: ${match[2]}`);
    }
    if (scripts.includes(match)) localScripts++;
  }

  if (localScripts === 0) {
    throw new Error('The replay viewer has no local entry script.');
  }
  return revision;
}

try {
  const revision = verifyReplay(process.argv[2], process.argv[3]);
  console.log(`Retaining deployed replay viewer ${revision}.`);
} catch (error) {
  console.error(`::error title=Existing replay viewer invalid::${error.message}`);
  process.exitCode = 1;
}
