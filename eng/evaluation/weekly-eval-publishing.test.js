const test = require('node:test');
const assert = require('node:assert/strict');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const workflowPath = path.join(
  __dirname, '..', '..', '.github', 'workflows', 'weekly-eval-insights.md',
);
const workflow = fs.readFileSync(workflowPath, 'utf8');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

function stepScript(name) {
  const lines = workflow.split(/\r?\n/);
  const step = lines.findIndex(line => line.trim() === `- name: ${name}`);
  assert.notEqual(step, -1, `Missing step: ${name}`);
  const stepIndent = lines[step].length - lines[step].trimStart().length;
  let script = step + 1;
  while (script < lines.length && lines[script].trim() !== 'script: |') {
    const line = lines[script];
    if (line.trim() && line.length - line.trimStart().length <= stepIndent) {
      assert.fail(`Missing script for step: ${name}`);
    }
    script += 1;
  }
  assert.ok(script < lines.length, `Missing script for step: ${name}`);
  const indent = lines[script].length - lines[script].trimStart().length + 2;
  const body = [];
  for (let index = script + 1; index < lines.length; index += 1) {
    const line = lines[index];
    if (line.trim() && line.length - line.trimStart().length < indent) break;
    body.push(line.slice(indent));
  }
  return body.join('\n');
}

const detectionScript = stepScript('Include weekly report in threat detection');
const publisherScript = stepScript('Publish weekly evaluation report');

function digest(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}

function report(size = 23409) {
  const headings = [
    '### For week 2026-10-30 to 2026-11-06',
    '### High-level stats',
    '### Executive insights',
    '### Recommendations',
    '### Trends',
    '### Plugin and skill details',
    '### Methodology and evidence limits',
  ].join('\n\n');
  const prefix = `${headings}\n\nEvidence for @example.\n`;
  assert.ok(size >= Buffer.byteLength(prefix));
  return Buffer.from(prefix + 'x'.repeat(size - Buffer.byteLength(prefix)));
}

function fixture(t, options = {}) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'weekly-eval-publishing-'));
  const agentDir = path.join(root, 'agent');
  const reportPath = path.join(agentDir, 'weekly-eval-report.md');
  const outputPath = path.join(root, 'agent_output.json');
  const detectionPath = path.join(root, 'detection.json');
  fs.mkdirSync(agentDir);

  const bytes = options.bytes ?? report();
  const item = {
    type: 'publish_weekly_eval_report',
    week_key: '2026-W45',
    sha256: digest(bytes),
    ...options.item,
  };
  const output = { items: options.items ?? [item] };
  fs.writeFileSync(reportPath, bytes);
  fs.writeFileSync(outputPath, JSON.stringify(output));
  fs.writeFileSync(detectionPath, JSON.stringify(output));

  t.after(() => {
    for (const file of [reportPath, outputPath, detectionPath]) {
      if (!fs.existsSync(file)) continue;
      if (fs.lstatSync(file).isDirectory()) fs.rmdirSync(file);
      else fs.unlinkSync(file);
    }
    fs.rmdirSync(agentDir);
    fs.rmdirSync(root);
  });

  return { root, reportPath, outputPath, detectionPath, bytes, item };
}

function mockGithub(options = {}) {
  const calls = { issues: [], comments: [] };
  const listForRepo = async () => {};
  const listComments = async () => {};
  const github = {
    rest: {
      actions: {
        getWorkflowRun: async () => ({
          data: { created_at: '2026-11-06T17:00:00Z' },
        }),
      },
      issues: {
        listForRepo,
        listComments,
        create: async input => {
          calls.issues.push(input);
          return { data: { number: 17 } };
        },
        createComment: async input => {
          calls.comments.push(input);
          return { data: {} };
        },
      },
    },
    paginate: async method => {
      if (method === listForRepo) return options.issues ?? [];
      if (method === listComments) return options.comments ?? [];
      throw new Error('Unexpected paginated request');
    },
  };
  return { github, calls };
}

function mockCore() {
  return {
    failures: [],
    messages: [],
    setFailed(message) { this.failures.push(message); },
    info(message) { this.messages.push(message); },
  };
}

async function detect(f) {
  const execute = new AsyncFunction('require', 'process', detectionScript);
  await execute(require, {
    env: { REPORT_PATH: f.reportPath, DETECTION_INPUT_PATH: f.detectionPath },
  });
}

async function publish(f, options = {}) {
  const { github, calls } = mockGithub(options);
  const core = mockCore();
  const execute = new AsyncFunction(
    'require', 'process', 'core', 'github', 'context', publisherScript,
  );
  await execute(
    require,
    { env: {
      GH_AW_AGENT_OUTPUT: f.outputPath,
      EXPECTED_REPOSITORY: 'dotnet/skills',
      GH_AW_ACTIONS_DIR: process.env.GH_AW_ACTIONS_DIR,
    } },
    core,
    github,
    { repo: { owner: 'dotnet', repo: 'skills' }, runId: 123 },
  );
  return { core, calls };
}

function dashboard(number = 17) {
  return {
    number,
    user: { login: 'github-actions[bot]' },
    title: 'Weekly Evaluation Health',
    body: '<!-- weekly-eval-insights-dashboard:v1 -->',
  };
}

test('scans and publishes the same 23,409-byte report', async t => {
  const f = fixture(t);
  await detect(f);

  const scanned = JSON.parse(fs.readFileSync(f.detectionPath, 'utf8'));
  const original = JSON.parse(fs.readFileSync(f.outputPath, 'utf8'));
  assert.equal(scanned.items[0].body, f.bytes.toString('utf8'));
  assert.equal(scanned.items[0].sha256, digest(f.bytes));
  assert.equal(original.items[0].body, undefined);

  const { core, calls } = await publish(f);
  assert.deepEqual(core.failures, []);
  assert.equal(calls.issues.length, 1);
  assert.equal(calls.comments.length, 1);
  assert.equal(
    calls.comments[0].body,
    `${f.bytes.toString('utf8').replace('@example', '&#64;example')}` +
      '\n\n<!-- weekly-eval-insights:2026-W45 -->',
  );
});

test('does not publish a report changed after detection', async t => {
  const f = fixture(t);
  await detect(f);
  fs.writeFileSync(f.reportPath, report(12000));
  const { core, calls } = await publish(f);
  assert.match(core.failures[0], /digest is invalid/);
  assert.deepEqual(calls, { issues: [], comments: [] });
});

test('rejects missing and malformed report outputs', async t => {
  await t.test('missing safe output', async t => {
    const f = fixture(t, { items: [] });
    await assert.rejects(detect(f), /Expected exactly one weekly report output/);
    const { core, calls } = await publish(f);
    assert.match(core.failures[0], /only output item/);
    assert.deepEqual(calls, { issues: [], comments: [] });
  });

  await t.test('multiple safe outputs', async t => {
    const f = fixture(t, { items: [{ type: 'noop' }, { type: 'noop' }] });
    await assert.rejects(detect(f), /Expected exactly one weekly report output/);
    const { core } = await publish(f);
    assert.match(core.failures[0], /only output item/);
  });

  await t.test('missing report file', async t => {
    const f = fixture(t);
    fs.unlinkSync(f.reportPath);
    await assert.rejects(detect(f), { code: 'ENOENT' });
    await assert.rejects(publish(f), { code: 'ENOENT' });
  });

  await t.test('report path is a directory', async t => {
    const f = fixture(t);
    fs.unlinkSync(f.reportPath);
    fs.mkdirSync(f.reportPath);
    await assert.rejects(detect(f), /invalid, or oversized/);
    const { core } = await publish(f);
    assert.match(core.failures[0], /invalid, or oversized/);
  });

  await t.test('malformed digest', async t => {
    const f = fixture(t, { item: { sha256: 'not-a-digest' } });
    await assert.rejects(detect(f), /digest is invalid/);
    const { core } = await publish(f);
    assert.match(core.failures[0], /digest is invalid/);
  });

  await t.test('invalid UTF-8', async t => {
    const f = fixture(t, { bytes: Buffer.concat([report(), Buffer.from([0xff])]) });
    await assert.rejects(detect(f), /encoding or digest is invalid/);
    const { core } = await publish(f);
    assert.match(core.failures[0], /encoding or digest is invalid/);
  });

  await t.test('oversized UTF-8 report', async t => {
    const f = fixture(t, { bytes: Buffer.from('é'.repeat(32501)) });
    await assert.rejects(detect(f), /invalid, or oversized/);
    const { core } = await publish(f);
    assert.match(core.failures[0], /invalid, or oversized/);
  });
});

test('validates the week, headings, and reserved marker before writing', async t => {
  const cases = [
    { item: { week_key: '2026-W44' } },
    { item: { week_key: '2026-W99' } },
    { bytes: Buffer.from('x'.repeat(600)) },
    { bytes: Buffer.concat([report(), Buffer.from('\n<!-- weekly-eval-insights:fake -->')]) },
  ];
  for (const options of cases) {
    await t.test(JSON.stringify(options.item ?? { invalidBody: true }), async t => {
      const f = fixture(t, options);
      const { core, calls } = await publish(f);
      assert.match(core.failures[0], /input failed validation/);
      assert.deepEqual(calls, { issues: [], comments: [] });
    });
  }
});

test('sanitizes links, HTML, and control characters before posting', async t => {
  const hostile = [
    '',
    '<details><summary>Plugin</summary>kept</details>',
    'hidden<!-- weekly marker spoof -->end',
    '<img src=x onerror=alert(1)> <a href="https://evil.example">a</a>',
    '[run](https://github.com/dotnet/skills/actions/runs/1)',
    '[bad](https://evil.example/p) www.evil.example //evil.example/x',
    'https://github.com.evil.example/ https://github.com@evil.example/',
    '<!<!-- x -->-- spoof -->',
    'red\u001b[31m text\u0007 \u202eltr',
  ].join('\n');
  const bytes = Buffer.concat([report(1000), Buffer.from(hostile)]);
  const f = fixture(t, { bytes });
  await detect(f);
  const { core, calls } = await publish(f);
  assert.deepEqual(core.failures, []);
  const posted = calls.comments[0].body.replace(/\n\n<!-- weekly-eval-insights:2026-W45 -->$/, '');
  assert.ok(posted.includes('<details><summary>Plugin</summary>kept</details>'));
  assert.ok(posted.includes('hiddenend'));
  assert.ok(posted.includes('&lt;img src=x onerror=alert(1)>'));
  assert.ok(posted.includes('&lt;a href="(external link removed)">'));
  assert.ok(posted.includes('[run](https://github.com/dotnet/skills/actions/runs/1)'));
  assert.ok(posted.includes('[bad]((external link removed))'));
  assert.ok(posted.includes('&lt;!-- spoof -->'));
  assert.ok(posted.includes('red text ltr'));
  assert.doesNotMatch(posted, /<!--|evil\.example|<img|<a /);
});

test('rejects a comment that grows too large after mention escaping', async t => {
  const bytes = Buffer.concat([report(1000), Buffer.from(' @x'.repeat(19000))]);
  const f = fixture(t, { bytes });
  await detect(f);
  const { core, calls } = await publish(f);
  assert.match(core.failures[0], /comment size limit/);
  assert.deepEqual(calls, { issues: [], comments: [] });
});

test('filters encoded inline, image, and reference destinations', async t => {
  const destinations = [
    'https:&#47;&#47;evil.example/report',
    'https&#58;&#x2f;&#x2f;evil.example/report',
    'https&colon;&sol;&sol;evil.example/report',
    'https:&amp;#47;&amp;#47;evil.example/report',
    String.raw`https:\/\/evil.example/report`,
    String.raw`https:\\evil.example/report`,
    'https:/evil.example/report',
    'http:evil.example/report',
    'http&#58;evil.example/report',
    'https://github.com&#9;@evil.example/report',
  ];
  for (const destination of destinations) {
    await t.test(destination, async t => {
      const suffix = [
        `[evidence](${destination})`,
        `![image](${destination})`,
        '[reference][external]',
        `[external]: <${destination}>`,
      ].join('\n');
      const f = fixture(t, { bytes: Buffer.concat([report(1000), Buffer.from(suffix)]) });
      const { core, calls } = await publish(f);
      assert.deepEqual(core.failures, []);
      const posted = calls.comments[0].body.replace('&#64;example', '@example');
      assert.doesNotMatch(posted, /&#(?:\d+|x[\da-f]+);|&(?:colon|sol|amp);|https?:[/\\]+evil\.example/i);
      assert.ok(
        posted.includes('(external link removed)') ||
        posted.includes('[encoded character removed]'),
      );
    });
  }
});

test('filters GFM autolinks after Markdown emphasis delimiters', async t => {
  for (const prefix of ['_', '*', '~', '(']) {
    await t.test(prefix, async t => {
      const f = fixture(t, {
        bytes: Buffer.concat([report(1000), Buffer.from(`\n${prefix}www.evil.example/report`)]),
      });
      const { core, calls } = await publish(f);
      assert.deepEqual(core.failures, []);
      assert.doesNotMatch(calls.comments[0].body, /www\.evil\.example/);
      assert.ok(calls.comments[0].body.includes('(external link removed)'));
    });
  }
});

test('preserves an encoded GitHub link after normalization', async t => {
  const f = fixture(t, {
    bytes: Buffer.concat([
      report(1000),
      Buffer.from('\n[run](https:&#47;&#47;github.com/dotnet/skills/actions/runs/1)'),
    ]),
  });
  const { core, calls } = await publish(f);
  assert.deepEqual(core.failures, []);
  assert.ok(calls.comments[0].body.includes(
    '[run](https://github.com/dotnet/skills/actions/runs/1)',
  ));
});

test('enforces the report byte limits at their boundaries', async t => {
  for (const size of [499, 500, 65000, 65001]) {
    await t.test(`${size} bytes`, async t => {
      const f = fixture(t, { bytes: report(size) });
      if (size < 500 || size > 65000) {
        await assert.rejects(detect(f), /invalid, or oversized/);
        const { core, calls } = await publish(f);
        assert.match(core.failures[0], /invalid, or oversized/);
        assert.deepEqual(calls, { issues: [], comments: [] });
      } else {
        await detect(f);
        const { core, calls } = await publish(f);
        assert.deepEqual(core.failures, []);
        assert.equal(calls.comments.length, 1);
      }
    });
  }
});

test('treats a bot-authored report for the same week as success', async t => {
  const f = fixture(t);
  const { core, calls } = await publish(f, {
    issues: [dashboard()],
    comments: [{
      user: { login: 'github-actions[bot]' },
      body: 'Existing report\n\n<!-- weekly-eval-insights:2026-W45 -->',
    }],
  });
  assert.deepEqual(core.failures, []);
  assert.match(core.messages[0], /already exists/);
  assert.deepEqual(calls, { issues: [], comments: [] });
});

test('rejects multiple matching dashboard issues', async t => {
  const f = fixture(t);
  const { core, calls } = await publish(f, {
    issues: [dashboard(17), dashboard(18)],
  });
  assert.match(core.failures[0], /Multiple open/);
  assert.deepEqual(calls, { issues: [], comments: [] });
});

test('declares fail-closed scan and publication checks', () => {
  assert.match(workflow, /threat-detection:\s*\n\s*continue-on-error: false/);
  assert.match(workflow, /artifacts:\s*\n\s*- \/tmp\/gh-aw\/agent\/weekly-eval-report\.md/);
  assert.match(workflow, /\n  edit: false\n/);
  assert.match(
    workflow,
    /args:\s*\n\s*- --allow-tool\s*\n\s*- "write\(\/tmp\/gh-aw\/agent\/weekly-eval-report\.md\)"/,
  );
  assert.match(workflow, /needs: \[agent, publish_weekly_eval_report\]/);
  assert.match(workflow, /PUBLICATION_RESULT.*needs\.publish_weekly_eval_report\.result/);
  assert.match(workflow, /\[ "\$PUBLICATION_RESULT" != "success" \]/);
});

test('limits the compiled agent write permission to the report file', () => {
  const lock = fs.readFileSync(workflowPath.replace(/\.md$/, '.lock.yml'), 'utf8');
  const agentStart = lock.indexOf('\n  agent:\n');
  const nextJob = lock.indexOf('\n  conclusion:\n', agentStart);
  assert.ok(agentStart !== -1 && nextJob > agentStart);
  const agent = lock.slice(agentStart, nextJob);
  assert.match(agent, /write\(\/tmp\/gh-aw\/agent\/weekly-eval-report\.md\)/);
  assert.doesNotMatch(agent, /--allow-all-paths|--allow-tool\s+[\\'"]*write(?:[\s\\'"]|$)/);
});

test('uploads fallback output only after successful secret redaction', () => {
  const lock = fs.readFileSync(workflowPath.replace(/\.md$/, '.lock.yml'), 'utf8');
  const fallbackUpload = lock.match(
    /- name: Upload agent output fallback artifact\n([\s\S]*?)(?=\n      - name: Upload agent artifacts)/,
  );
  assert.ok(fallbackUpload, 'Missing fallback artifact upload');
  assert.match(
    fallbackUpload[1],
    /if: always\(\) && steps\.redact_secrets\.outcome == 'success'/,
  );
});

test('augments the input directory consumed by the compiled detector', () => {
  const lock = fs.readFileSync(workflowPath.replace(/\.md$/, '.lock.yml'), 'utf8');
  const prepare = lock.indexOf('bash "${RUNNER_TEMP}/gh-aw/actions/prepare_threat_detection_files.sh"');
  const augment = lock.indexOf('- name: Include weekly report in threat detection', prepare);
  const invocation = lock.indexOf('threat-detect --engine', augment);
  assert.ok(prepare !== -1 && augment > prepare && invocation > augment);
  const augmentation = lock.slice(augment, invocation);
  const input = augmentation.match(/DETECTION_INPUT_PATH: ([^\r\n]+)/);
  assert.ok(input, 'Missing detector input path');
  const directory = path.posix.dirname(input[1].trim());
  const detectorCommand = lock.slice(invocation).split(/\r?\n/, 1)[0];
  assert.ok(detectorCommand.includes(`${directory}'`));
});
