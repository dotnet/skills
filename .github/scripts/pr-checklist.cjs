const MARKER = '<!-- pr-action-checklist -->';
const BOT = 'github-actions[bot]';

async function resolvePullRequests({ github, context }) {
  const { payload, eventName, repo } = context;
  if (eventName === 'issue_comment') {
    if (!payload.issue.pull_request || payload.comment.body.trim() !== '/checklist' ||
        payload.comment.user.type === 'Bot') return [];
    const { data: pr } = await github.rest.pulls.get({ ...repo, pull_number: payload.issue.number });
    const author = payload.comment.user.login === pr.user.login;
    const collaborator = ['OWNER', 'MEMBER', 'COLLABORATOR'].includes(payload.comment.author_association);
    return pr.state === 'open' && (author || collaborator) ? [pr.number] : [];
  }
  if (payload.pull_request) return payload.pull_request.state === 'open' ? [payload.pull_request.number] : [];
  if (eventName !== 'schedule') return [];
  const prs = await github.paginate(github.rest.pulls.list, { ...repo, state: 'open', per_page: 100 });
  return prs.map(pr => pr.number);
}

function renderChecklist({ pr, checks, statuses, review, unresolved }) {
  const escape = value => String(value).replace(/[\r\n]/g, ' ').replace(/&/g, '&amp;')
    .replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/\\/g, '&#92;')
    .replace(/\[/g, '&#91;').replace(/\]/g, '&#93;').replace(/\*/g, '&#42;')
    .replace(/_/g, '&#95;').replace(/`/g, '&#96;').replace(/@/g, '&#64;');
  const lines = [MARKER, '## PR action checklist', '',
    `Status for commit \`${pr.head.sha}\`. This is a progress summary, not a merge authorization.`, ''];
  const item = (done, text) => lines.push(`- [${done ? 'x' : ' '}] ${text}`);
  item(!pr.draft, 'Author: mark the PR ready for review.');
  item(pr.mergeable === true, pr.mergeable === false
    ? 'Author: resolve merge conflicts.'
    : pr.mergeable === null ? 'Mergeability is still being computed; refresh this checklist.'
      : 'No merge conflicts reported.');
  const tasks = [...(pr.body ?? '').replace(/<!--[^]*?-->|```[^]*?```|~~~[^]*?~~~/g, '')
    .matchAll(/^\s*[-*] \[([ xX])\] (.*)$/gm)];
  const pending = tasks.filter(task => task[1] === ' ').length;
  if (tasks.length) {
    lines.push('', '### PR description checklist', '',
      `${pending} unchecked of ${tasks.length}; update applicable items in the [PR description](${pr.html_url}) and mark inapplicable items there.`, '');
    for (const task of tasks) item(task[1] !== ' ', escape(task[2]));
  }

  lines.push('', '### Validation (current commit)', '');
  const latest = new Map();
  for (const check of checks) {
    const key = `${check.app?.id}:${check.name}`;
    if (!latest.has(key) || latest.get(key).id < check.id) latest.set(key, check);
  }
  for (const check of [...latest.values()].sort((a, b) => a.name.localeCompare(b.name))) {
    const passed = check.status === 'completed' && ['success', 'neutral', 'skipped'].includes(check.conclusion);
    const state = check.status === 'completed' ? check.conclusion : check.status;
    item(passed, `${escape(check.name)}: ${escape(state)}. See the [Checks tab](${pr.html_url}/checks).`);
  }
  const latestStatuses = new Map();
  for (const status of statuses) {
    if (!latestStatuses.has(status.context) || latestStatuses.get(status.context).id < status.id) {
      latestStatuses.set(status.context, status);
    }
  }
  for (const status of [...latestStatuses.values()].sort((a, b) => a.context.localeCompare(b.context))) {
    item(status.state === 'success', `${escape(status.context)}: ${escape(status.state)}. See the [Checks tab](${pr.html_url}/checks).`);
  }
  if (!latest.size && !latestStatuses.size) item(false, 'Wait for validation checks to report on this commit.');
  if (!latestStatuses.has('evaluation-status')) item(false, 'Wait for evaluation-status to report whether skill evaluation is required.');
  const evaluation = latestStatuses.get('evaluation-status');
  if (evaluation && evaluation.state !== 'success') {
    lines.push('', `Skill evaluation: a maintainer can submit a review containing \`/evaluate\`, or comment \`/evaluate ${pr.head.sha}\`. If evaluation is already running, wait for its result.`);
  }
  lines.push('', '### Review', '');
  item(review === 'APPROVED', review === 'CHANGES_REQUESTED'
    ? 'Author: address the requested review changes and request another review.'
    : review === 'APPROVED' ? 'Required review approval recorded.' : 'Maintainer: review and approve the PR where required.');
  item(unresolved === 0, `Address and resolve review conversations (${unresolved} unresolved).`);
  lines.push('', 'The PR author or a repository collaborator can comment `/checklist` to refresh this comment.',
    'It also refreshes on PR updates and on a scheduled reconciliation every 30 minutes.',
    'Only checks reported by GitHub are listed; skipped/neutral checks are marked complete, not passed. Branch protection remains authoritative.');
  return lines.join('\n');
}

async function updateChecklist({ github, context, core, number }) {
  const repo = context.repo;
  const { data: pr } = await github.rest.pulls.get({ ...repo, pull_number: number });
  if (pr.state !== 'open') return;
  const files = await github.paginate(github.rest.pulls.listFiles, { ...repo, pull_number: number, per_page: 100 });
  if (!files.some(file => /^plugins\/[^/]+\/skills\//.test(file.filename) ||
      /^plugins\/[^/]+\/skills\//.test(file.previous_filename ?? '') ||
      /^tests\/[^/]+\/[^/]+\//.test(file.filename))) return;

  const [checks, statuses, comments] = await Promise.all([
    github.paginate(github.rest.checks.listForRef, { ...repo, ref: pr.head.sha, per_page: 100 }),
    github.paginate(github.rest.repos.listCommitStatusesForRef, { ...repo, ref: pr.head.sha, per_page: 100 }),
    github.paginate(github.rest.issues.listComments, { ...repo, issue_number: number, per_page: 100 }),
  ]);
  let cursor = null;
  let review;
  let unresolved = 0;
  do {
    const data = await github.graphql(`query($owner: String!, $repo: String!, $number: Int!, $cursor: String) {
      repository(owner: $owner, name: $repo) {
        pullRequest(number: $number) {
          reviewDecision
          reviewThreads(first: 100, after: $cursor) {
            nodes { isResolved }
            pageInfo { hasNextPage endCursor }
          }
        }
      }
    }`, { ...repo, number, cursor });
    const result = data.repository.pullRequest;
    review = result.reviewDecision;
    unresolved += result.reviewThreads.nodes.filter(thread => !thread.isResolved).length;
    cursor = result.reviewThreads.pageInfo.hasNextPage ? result.reviewThreads.pageInfo.endCursor : null;
  } while (cursor);

  const body = renderChecklist({ pr, checks, statuses, review, unresolved });
  const { data: current } = await github.rest.pulls.get({ ...repo, pull_number: number });
  if (current.state !== 'open' || current.head.sha !== pr.head.sha) {
    core.info('PR changed while collecting checklist data; leaving the next event to refresh it.');
    return;
  }
  const existing = comments.find(comment => comment.user.login === BOT && comment.body?.startsWith(MARKER));
  if (existing) {
    if (existing.body !== body) await github.rest.issues.updateComment({ ...repo, comment_id: existing.id, body });
  } else {
    await github.rest.issues.createComment({ ...repo, issue_number: number, body });
  }
}

module.exports = { MARKER, resolvePullRequests, renderChecklist, updateChecklist };
