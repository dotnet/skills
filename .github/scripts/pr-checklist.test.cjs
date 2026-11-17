const test = require('node:test');
const assert = require('node:assert/strict');
const { MARKER, resolvePullRequests, renderChecklist, updateChecklist } = require('./pr-checklist.cjs');

const repo = { owner: 'dotnet', repo: 'skills' };
const pr = {
  number: 562, state: 'open', user: { login: 'author' }, draft: false, mergeable: true,
  head: { sha: 'a'.repeat(40) }, html_url: 'https://github.com/dotnet/skills/pull/562',
  body: '- [x] Tested\n- [ ] Documented',
};
const success = { id: 2, context: 'evaluation-status', state: 'success' };
const render = overrides => renderChecklist({ pr, checks: [], statuses: [success], review: 'APPROVED', unresolved: 0, ...overrides });

test('checklist shows pending author, validation, and review actions with exact-head eval guidance', () => {
  const body = render({ pr: { ...pr, draft: true, mergeable: false },
    checks: [{ id: 1, name: 'skill-check', status: 'completed', conclusion: 'failure' }],
    statuses: [{ ...success, state: 'pending' }], review: 'CHANGES_REQUESTED', unresolved: 2 });
  for (const text of ['[ ] Author: mark', '[ ] Author: resolve merge conflicts', '1 unchecked of 2',
    '[ ] Documented', '[x] Tested', '[ ] skill-check: failure', '[ ] evaluation-status: pending', '[ ] Author: address the requested review changes',
    '[ ] Address and resolve review conversations (2 unresolved)', `/evaluate ${pr.head.sha}`, '`/checklist`']) {
    assert.ok(body.includes(text), text);
  }
});

test('newest reruns and commit statuses win, with separate check app identities', () => {
  const body = render({ checks: [
    { id: 4, name: 'lint', app: { id: 1 }, status: 'completed', conclusion: 'success' },
    { id: 1, name: 'lint', app: { id: 1 }, status: 'completed', conclusion: 'failure' },
    { id: 3, name: 'lint', app: { id: 2 }, status: 'in_progress' },
    { id: 5, name: 'optional', status: 'completed', conclusion: 'skipped' },
  ], statuses: [{ ...success, id: 1, state: 'failure' }, success] });
  assert.match(body, /\[x\] lint: success/);
  assert.match(body, /\[ \] lint: in&#95;progress/);
  assert.match(body, /\[x\] optional: skipped/);
  assert.match(body, /\[x\] evaluation-status: success/);
  assert.doesNotMatch(body, /: failure/);
  assert.doesNotMatch(body, /\/evaluate /);
});

test('missing checks, unknown mergeability, and missing reviews remain unchecked', () => {
  const body = render({ pr: { ...pr, mergeable: null }, statuses: [], review: null });
  assert.match(body, /\[ \] Wait for validation/);
  assert.match(body, /\[ \] Wait for evaluation-status/);
  assert.match(body, /\[ \] Mergeability is still/);
  assert.match(body, /\[ \] Maintainer: review/);
});

test('does not count fenced sample checklists or reproduce untrusted author text', () => {
  const body = render({ pr: { ...pr, body: '```md\n- [ ] example\n```\n- [X] Done\n- [ ] @someone <script>bad</script>' },
    checks: [{ id: 1, name: 'bad\n- [x] @someone <script>`', status: 'queued' }] });
  assert.match(body, /1 unchecked of 2/);
  assert.doesNotMatch(body, /@someone|<script>|bad<\/script>/);
  assert.match(body, /&#64;someone &lt;script&gt;&#96;/);
});

function client(overrides = {}) {
  const writes = [];
  const reads = [];
  const rest = {
    pulls: { get: async () => ({ data: pr }), listFiles: 'files', list: 'prs' },
    checks: { listForRef: 'checks' },
    repos: { listCommitStatusesForRef: 'statuses' },
    issues: {
      listComments: 'comments',
      createComment: async args => writes.push({ kind: 'create', ...args }),
      updateComment: async args => writes.push({ kind: 'update', ...args }),
    },
  };
  const github = {
    rest,
    paginate: async (method, args) => {
      reads.push({ method, args });
      return ({ files: [{ filename: 'plugins/dotnet/skills/example/SKILL.md' }],
        checks: [], statuses: [success], comments: [], prs: [pr], ...overrides })[method];
    },
    graphql: async (_query, args) => {
      reads.push({ method: 'graphql', args });
      return { repository: { pullRequest: { reviewDecision: 'APPROVED', reviewThreads: {
        nodes: [{ isResolved: true }], pageInfo: { hasNextPage: false, endCursor: null },
      } } } };
    },
  };
  return { github, writes, reads, context: { repo }, core: { info: () => {} }, number: pr.number };
}

test('creates a single checklist, updates only the bot-owned comment, and skips unchanged content', async () => {
  const first = client();
  await updateChecklist(first);
  assert.equal(first.writes.length, 1);
  assert.equal(first.writes[0].kind, 'create');
  const body = first.writes[0].body;
  const comments = [
    { id: 1, user: { login: 'author' }, body: MARKER },
    { id: 2, user: { login: 'github-actions[bot]' }, body: `${MARKER}\nold` },
  ];
  const second = client({ comments });
  await updateChecklist(second);
  assert.equal(second.writes.length, 1);
  assert.equal(second.writes[0].kind, 'update');
  assert.equal(second.writes[0].comment_id, 2);
  assert.equal(second.writes[0].body, body);
  const third = client({ comments: [{ ...comments[1], body }] });
  await updateChecklist(third);
  assert.deepEqual(third.writes, []);
  for (const read of first.reads.filter(read => ['checks', 'statuses'].includes(read.method))) {
    assert.equal(read.args.ref, pr.head.sha);
    assert.equal(read.args.per_page, 100);
  }
});

test('only skill or skill-test changes are in scope, including renamed skill files', async () => {
  const unrelated = client({ files: [{ filename: 'README.md' }] });
  await updateChecklist(unrelated);
  assert.deepEqual(unrelated.writes, []);
  assert.equal(unrelated.reads.length, 1);
  for (const file of [
    { filename: 'archive/old.md', previous_filename: 'plugins/dotnet/skills/old/SKILL.md' },
    { filename: 'tests/dotnet/example/eval.yaml' },
  ]) {
    const c = client({ files: [file] });
    await updateChecklist(c);
    assert.equal(c.writes.length, 1);
  }
});

test('reads all review thread pages and reports unresolved threads', async () => {
  const c = client();
  const cursors = [];
  c.github.graphql = async (_query, args) => {
    cursors.push(args.cursor);
    return { repository: { pullRequest: { reviewDecision: 'REVIEW_REQUIRED', reviewThreads: {
      nodes: [{ isResolved: false }],
      pageInfo: { hasNextPage: !args.cursor, endCursor: args.cursor ? null : 'next' },
    } } } };
  };
  await updateChecklist(c);
  assert.deepEqual(cursors, [null, 'next']);
  assert.match(c.writes[0].body, /2 unresolved/);
});

test('does not publish stale-head or closed PR results', async () => {
  for (const current of [{ ...pr, head: { sha: 'b'.repeat(40) } }, { ...pr, state: 'closed' }]) {
    const c = client();
    let calls = 0;
    c.github.rest.pulls.get = async () => ({ data: calls++ === 0 ? pr : current });
    await updateChecklist(c);
    assert.deepEqual(c.writes, []);
  }
});

test('API failures propagate rather than posting a success-shaped summary', async () => {
  const c = client();
  c.github.graphql = async () => { throw new Error('GitHub unavailable'); };
  await assert.rejects(updateChecklist(c), /GitHub unavailable/);
  assert.deepEqual(c.writes, []);
});

test('refresh command permits a fork author or collaborator but rejects others and bots', async () => {
  for (const [login, type, association, allowed] of [
    ['author', 'User', 'NONE', true], ['maintainer', 'User', 'MEMBER', true],
    ['outsider', 'User', 'NONE', false], ['author', 'Bot', 'MEMBER', false],
  ]) {
    const c = client();
    c.context.eventName = 'issue_comment';
    c.context.payload = { issue: { number: pr.number, pull_request: {} },
      comment: { body: '/checklist\n', user: { login, type }, author_association: association } };
    assert.deepEqual(await resolvePullRequests(c), allowed ? [562] : []);
  }
});

test('ignores unrelated commands, issues, and closed PRs; routes PR and scheduled events', async () => {
  const c = client();
  c.context.eventName = 'issue_comment';
  c.context.payload = { issue: { number: 562 }, comment: { body: '/checklist' } };
  assert.deepEqual(await resolvePullRequests(c), []);
  c.context.payload.issue.pull_request = {};
  c.context.payload.comment.body = '/checklist-extra';
  assert.deepEqual(await resolvePullRequests(c), []);
  c.context.eventName = 'pull_request_target';
  c.context.payload = { pull_request: pr };
  assert.deepEqual(await resolvePullRequests(c), [562]);
  c.context.payload = { pull_request: { ...pr, state: 'closed' } };
  assert.deepEqual(await resolvePullRequests(c), []);
  c.context.eventName = 'schedule';
  c.context.payload = {};
  assert.deepEqual(await resolvePullRequests(c), [562]);
  assert.deepEqual(c.reads.at(-1), { method: 'prs', args: { ...repo, state: 'open', per_page: 100 } });
});
