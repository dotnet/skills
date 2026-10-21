const fs = require('node:fs');
const path = require('node:path');

const argv = process.argv.slice(2);
let stdout = '';
let stderr = '';
let exitCode = 0;

if (argv[0] === 'dnx') {
  stdout = 'Starting the binlog MCP launcher.\n';
  stderr = 'A version higher than 0.0.0-0 of package microsoft.aitools.binlogmcp '
    + 'is not found in NuGet feeds approved-mirror.\n';
  exitCode = 1;
} else if (argv.length === 1 && ['--info', '--version'].includes(argv[0])) {
  stdout = '.NET SDK 10.0.402\n';
} else if (argv.join(' ') === 'nuget list source') {
  stdout = 'Registered Sources:\n  1. approved-mirror [Enabled]\n'
    + '     https://mirror.example.invalid/v3/index.json\n';
} else if (argv[0] === 'package' && argv[1] === 'search') {
  stdout = JSON.stringify({
    searchResult: [{ sourceName: 'approved-mirror', packages: [], problems: [] }],
    problems: [],
  }) + '\n';
} else {
  stderr = 'Unsupported offline launcher arguments: ' + JSON.stringify(argv) + '\n';
  exitCode = 64;
}

fs.appendFileSync(path.join(__dirname, 'launch-invocations.jsonl'), JSON.stringify({
  cwd: process.cwd(), argv, stdout, stderr, exitCode,
}) + '\n');
process.stdout.write(stdout);
process.stderr.write(stderr);
process.exitCode = exitCode;
