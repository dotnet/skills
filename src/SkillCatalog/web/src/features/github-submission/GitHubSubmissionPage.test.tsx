import '../../test/setup'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, expect, test, vi } from 'vitest'
import { githubSubmissionClient } from '../../api/githubSubmissionClient'
import type { SubmissionIntent } from '../../api/githubSubmissionModels'
import { GitHubSubmissionPage } from './GitHubSubmissionPage'

vi.mock('../../api/githubSubmissionClient', () => ({ githubSubmissionClient: {
  session: vi.fn(), start: vi.fn(), logout: vi.fn(), createIntent: vi.fn(), submit: vi.fn()
} }))

const file = new File(['skill'], 'SKILL.md')
const intent: SubmissionIntent = { id: 'intent', contributionType: 'NewSkill', targetRepository: 'dotnet/skills', destinationPath: 'plugins/dotnet/skills/sample', pullRequestTitle: 'Contribute sample', expiresAt: 'soon', files: [{ path: 'plugins/dotnet/skills/sample/SKILL.md', operation: 'add', sha256: 'hash', size: 5 }] }

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(githubSubmissionClient.session).mockResolvedValue({ authenticated: false })
  vi.mocked(githubSubmissionClient.start).mockResolvedValue({ authorizationUrl: 'https://github.com/login/oauth/authorize', transactionId: 'x', expiresAt: 'soon' })
})

function view() { return render(<MemoryRouter><GitHubSubmissionPage file={file} /></MemoryRouter>) }

test('keeps uploaded file available when popup is blocked', async () => {
  vi.spyOn(window, 'open').mockReturnValue(null)
  view()
  await userEvent.click(await screen.findByRole('button', { name: /sign in with github/i }))
  expect(screen.getByText(/popup was blocked/i)).toBeVisible()
  expect(screen.getByText(/uploaded package stays/i)).toBeVisible()
  expect(screen.getByRole('button', { name: /sign in with github/i })).toBeEnabled()
})

test('accepts only exact-origin popup completion and preserves upload', async () => {
  vi.spyOn(window, 'open').mockReturnValue({ closed: false } as Window)
  vi.mocked(githubSubmissionClient.session)
    .mockResolvedValueOnce({ authenticated: false })
    .mockResolvedValue({ authenticated: true, githubUserId: 42, login: 'octocat' })
  view()
  await screen.findByRole('button', { name: /sign in with github/i })
  window.dispatchEvent(new MessageEvent('message', { origin: 'https://evil.example', data: { type: 'skillcatalog:github-auth-complete' } }))
  expect(screen.getByRole('button', { name: /sign in with github/i })).toBeVisible()
  window.dispatchEvent(new MessageEvent('message', { origin: window.location.origin, data: { type: 'skillcatalog:github-auth-complete' } }))
  expect(await screen.findByText(/signed in as/i)).toHaveTextContent('octocat')
})

test('reviews, confirms, loads, and shows successful pull request', async () => {
  vi.mocked(githubSubmissionClient.session).mockResolvedValue({ authenticated: true, githubUserId: 42, login: 'octocat' })
  vi.mocked(githubSubmissionClient.createIntent).mockResolvedValue(intent)
  vi.mocked(githubSubmissionClient.submit).mockResolvedValue({ id: 'contribution', state: 'PullRequestOpen', pullRequestUrl: 'https://github.com/dotnet/skills/pull/7', updatedAt: 'now' })
  view()
  await userEvent.click(await screen.findByRole('button', { name: /prepare pull request/i }))
  expect(await screen.findByText('plugins/dotnet/skills/sample')).toBeVisible()
  expect(screen.getByRole('button', { name: /create pull request/i })).toBeDisabled()
  await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.click(screen.getByRole('button', { name: /create pull request/i }))
  expect(await screen.findByRole('link', { name: /open pull request/i })).toHaveAttribute('href', 'https://github.com/dotnet/skills/pull/7')
  expect(screen.getByRole('link', { name: /view contribution progress/i })).toBeVisible()
})

test('shows actionable preparation errors without discarding upload', async () => {
  vi.mocked(githubSubmissionClient.session).mockResolvedValue({ authenticated: true, githubUserId: 42, login: 'octocat' })
  vi.mocked(githubSubmissionClient.createIntent).mockRejectedValue(new Error('Contributor fork required. Create or synchronize your fork.'))
  view()
  await userEvent.click(await screen.findByRole('button', { name: /prepare pull request/i }))
  expect(await screen.findByText(/create or synchronize your fork/i)).toBeVisible()
  await waitFor(() => expect(screen.getByRole('button', { name: /prepare pull request/i })).toBeEnabled())
})
