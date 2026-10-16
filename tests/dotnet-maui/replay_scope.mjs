import { readFileSync } from 'node:fs';
import { captureWorkspaceBaseline } from '../../eng/evaluation-tools/node_modules/@microsoft/vally/dist/graders/static/workspace-diff.js';
import { DiffMatchGrader } from '../../eng/evaluation-tools/node_modules/@microsoft/vally/dist/graders/static/diff-match-grader.js';

const input = JSON.parse(readFileSync(0, 'utf8'));
if (input.action === 'capture') {
  console.log(JSON.stringify({
    baselineRef: await captureWorkspaceBaseline(input.workDir, input.baselineGitDir),
  }));
} else if (input.action === 'grade') {
  const result = await new DiffMatchGrader('diff-not-contains').grade({
    config: input.config,
    trajectory: {
      workDir: input.workDir,
      baselineGitDir: input.baselineGitDir,
      baselineRef: input.baselineRef,
    },
  });
  if (result.status === 'error') throw new Error(result.evidence);
  console.log(JSON.stringify(result));
} else {
  throw new Error(`Unknown scope replay action: ${input.action}`);
}
