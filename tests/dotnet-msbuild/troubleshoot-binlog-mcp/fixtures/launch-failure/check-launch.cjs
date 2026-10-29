const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const expected = {
  'NuGet.Config': 'b8ff5486232770da7b6bced4a4b829b7c9dc184961bad1c2b8c965f46f584b90',
  'affected-workspace/global.json': 'c9c9cae030d639a70a7f7642ee546c9a31dda4bae6492a3a8aa21d08cab83779',
  'affected-workspace/NuGet.Config': '3c086b57ae3b6b33cc26b35fd7c700db86f26e566976383796f82bbafb8d7fff',
  'affected-workspace/host.json': '09432624e12b77b7de4065aee62d6464860b8dc83fdaca8ab7fdc46ac2e6123e',
  'affected-workspace/dotnet.cjs': '68e972803b765d4b681cef76a23476bdc1e8adb17beffb3d484c11d826058edc',
};
for (const [file, hash] of Object.entries(expected)) {
  const content = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
  assert.equal(crypto.createHash('sha256').update(content).digest('hex'), hash,
    'Changed or missing diagnostic input: ' + file);
}
const cwd = fs.realpathSync.native('affected-workspace');
const records = fs.readFileSync(path.join(cwd, 'launch-invocations.jsonl'), 'utf8')
  .trim().split(/\r?\n/).map(line => JSON.parse(line));
const launches = records.filter(record => record.argv[0] === 'dnx');
assert.equal(launches.length, 1, 'Expected one actual launch, not search-only advice');
const launch = launches[0];
for (const record of records) {
  assert.equal(fs.realpathSync.native(record.cwd), cwd,
    'Launch must use the affected workspace, including SDK/feed diagnostics');
}
assert.ok(records.some(record => ['--version', '--info'].includes(record.argv[0])),
  'Missing workspace SDK inspection');
assert.ok(records.some(record => record.argv.join(' ') === 'nuget list source'),
  'Missing workspace feed inspection');
assert.deepEqual(launch.argv,
  ['dnx', 'Microsoft.AITools.BinlogMcp', '--yes', '--prerelease'],
  'Launch arguments must match the registered command without source overrides');
assert.equal(launch.exitCode, 1);
const result = JSON.parse(fs.readFileSync(path.join(cwd, 'launch-result.json'), 'utf8'));
for (const key of ['stdout', 'stderr', 'exitCode']) {
  assert.equal(result[key], launch[key], 'Missing or inaccurate captured ' + key);
}
assert.match(result.stderr, /not found in NuGet feeds approved-mirror/);

const inputPath = process.env.EVALUATE_GRADER_INPUT;
assert.ok(inputPath, 'Missing evaluator-owned trajectory input');
const input = JSON.parse(fs.readFileSync(inputPath, 'utf8'));
const events = input.trajectory?.events;
assert.ok(Array.isArray(events), 'Missing evaluator-owned trajectory events');
assert.ok(process.env.EVALUATE_WORKSPACE, 'Missing evaluator-owned workspace path');
const workspaceRoot = fs.realpathSync.native(process.env.EVALUATE_WORKSPACE);
const launcher = fs.realpathSync.native(path.join(cwd, 'dotnet.cjs'));
const shellTools = new Set([
  'bash', 'powershell', 'execute', 'shell', 'run_shell_command',
  'local_shell', 'run_in_terminal', 'run_command', 'terminal',
]);
const calls = new Map();
const completed = [];
for (const event of events) {
  const data = event.data ?? {};
  if (['tool_call', 'tool.execution_start'].includes(event.type)) {
    assert.ok(data.toolCallId && !calls.has(data.toolCallId), 'Duplicate or missing tool-call identity');
    let args = data.arguments;
    if (typeof args === 'string') args = JSON.parse(args);
    calls.set(data.toolCallId, {
      tool: (data.toolName ?? '').split(/[.:]/).at(-1).toLowerCase(),
      command: args?.command ?? '',
    });
  } else if (['tool_result', 'tool.execution_complete'].includes(event.type)) {
    const call = calls.get(data.toolCallId);
    if (call) completed.push({ ...call, result: data.result });
  }
}

function texts(value) {
  if (typeof value === 'string') {
    try {
      return [value, ...texts(JSON.parse(value))];
    } catch (error) {
      if (!(error instanceof SyntaxError)) throw error;
      return [value];
    }
  }
  return value && typeof value === 'object' ? Object.values(value).flatMap(texts) : [];
}

function executes(command, argv) {
  if (typeof command !== 'string') return false;
  const tokens = (command.match(/"(?:\\.|[^"\\])*"|'[^']*'|&&|[;&|\r\n]|[^\s;&|\r\n]+/g) ?? [])
    .map(token => token.replace(/^(['"])([\s\S]*)\1$/, '$2'));
  const commands = [[]];
  for (const token of tokens) {
    if (['&&', ';', '\r', '\n'].includes(token)) commands.push([]);
    else commands.at(-1).push(token);
  }
  const invocations = [];
  let directory = workspaceRoot;
  const samePath = (left, right) => process.platform === 'win32'
    ? left.toLowerCase() === right.toLowerCase() : left === right;
  for (const words of commands.filter(words => words.length)) {
    if (words[0] === '&') words.shift();
    if (['cd', 'set-location', 'push-location'].includes(words[0]?.toLowerCase())) {
      const target = words.length === 2 ? words[1] : words.length === 3
        && ['/d', '-path', '-literalpath'].includes(words[1].toLowerCase()) ? words[2] : null;
      if (!target || /[$`]/.test(target)) return false;
      const destination = path.resolve(directory, target);
      if (!fs.existsSync(destination)) return false;
      directory = fs.realpathSync.native(destination);
      continue;
    }
    const executable = words[0] ?? '';
    if (!['node', 'node.exe'].includes(executable.toLowerCase())
        && !samePath(path.resolve(directory, executable), process.execPath)) return false;
    const script = path.resolve(directory, words[1] ?? '');
    if (!samePath(directory, cwd) || !fs.existsSync(script)
        || !samePath(fs.realpathSync.native(script), launcher)) return false;
    invocations.push(words.slice(2));
  }
  return invocations.some(args => args.length === argv.length
    && argv.every((arg, index) => args[index] === arg));
}

for (const record of records) {
  assert.ok(completed.some(call => {
    if (!shellTools.has(call.tool) || !executes(call.command, record.argv)) return false;
    const observed = texts(call.result).map(text => text.replace(/\r\n/g, '\n'));
    return ['stdout', 'stderr'].every(key => !record[key]
      || observed.some(text => text.includes(record[key].replace(/\r\n/g, '\n').trim())));
  }), 'Missing correlated launcher execution evidence: ' + record.argv.join(' '));
}
