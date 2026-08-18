import '../../test/setup'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { githubSubmissionClient } from '../../api/githubSubmissionClient'
import type { Contribution } from '../../api/githubSubmissionModels'
import { ContributionStatusPage } from './ContributionStatusPage'

vi.mock('../../api/githubSubmissionClient', () => ({
  githubSubmissionClient: { contribution: vi.fn() }
}))

const contribution: Contribution = {
  id: 'abc',
  state: 'ChecksPending',
  pullRequestUrl: 'https://github.com/upstream/skills/pull/7',
  updatedAt: '2026-07-29T00:00:00Z',
  lastReconciledAt: '2026-07-29T00:00:00Z',
  evidence: [{ kind: 'check', label: 'Build', status: 'in_progress', url: 'https://checks/7' }]
}

test('shows lifecycle state, refresh age, evidence links, and manual reconciliation', async () => {
  vi.mocked(githubSubmissionClient.contribution).mockResolvedValue(contribution)
  render(<MemoryRouter initialEntries={['/contributions/abc']}><Routes>
    <Route path="/contributions/:contributionId" element={<ContributionStatusPage />} />
  </Routes></MemoryRouter>)

  expect(await screen.findByText(/current state:/i)).toHaveTextContent('Checks Pending')
  expect(screen.getByRole('link', { name: 'Build' })).toHaveAttribute('href', 'https://checks/7')
  await userEvent.click(screen.getByRole('button', { name: /refresh from github/i }))
  expect(githubSubmissionClient.contribution).toHaveBeenLastCalledWith('abc', true)
})


for (const state of ['Preparing','ForkReady','BranchReady','CommitReady','PullRequestOpen','ChecksPending','AwaitingReview','Merged','Closed','RecoveryRequired'] as const) {
  test(`renders ${state} lifecycle state`, async () => {
    vi.mocked(githubSubmissionClient.contribution).mockResolvedValue({
      id: 'abc', state, updatedAt: '2026-07-29T00:00:00Z',
      recoveryMessage: state === 'RecoveryRequired' ? 'Review the fork before retrying.' : undefined,
    })
    render(<MemoryRouter initialEntries={['/contributions/abc']}><Routes>
      <Route path="/contributions/:contributionId" element={<ContributionStatusPage />} />
    </Routes></MemoryRouter>)
    expect(await screen.findByText(/current state:/i)).toHaveTextContent(state.replace(/([a-z])([A-Z])/g, '$1 $2'))
    if (state === 'RecoveryRequired') expect(screen.getByText(/review the fork/i)).toBeVisible()
  })
}
