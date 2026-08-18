import '../../../test/setup'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, test, vi } from 'vitest'
import type { SubmissionIntent } from '../../../api/githubSubmissionModels'
import { SubmissionReview } from './SubmissionReview'

const intent: SubmissionIntent = {
  id: 'intent',
  contributionType: 'Update',
  targetRepository: 'dotnet/skills',
  destinationPath: 'plugins/dotnet/skills/sample',
  pullRequestTitle: 'Update sample skill',
  expiresAt: '2026-07-30T00:00:00Z',
  files: [
    { path: 'plugins/dotnet/skills/sample/new.txt', operation: 'add', sha256: 'a', size: 1 },
    { path: 'plugins/dotnet/skills/sample/SKILL.md', operation: 'change', sha256: 'b', size: 2 },
    { path: 'plugins/dotnet/skills/sample/old.txt', operation: 'delete', sha256: 'c', size: 3 },
  ],
}

test('labels updates, groups file operations, and requires explicit confirmation', async () => {
  const confirm = vi.fn()
  const submit = vi.fn()
  const { rerender } = render(<SubmissionReview intent={intent} confirmed={false} busy={false} onConfirmed={confirm} onSubmit={submit} />)

  expect(screen.getByText('Existing skill update')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Added files' })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Changed files' })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Removed files' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /create pull request/i })).toBeDisabled()
  await userEvent.click(screen.getByRole('checkbox'))
  expect(confirm).toHaveBeenCalledWith(true)

  rerender(<SubmissionReview intent={intent} confirmed busy={false} onConfirmed={confirm} onSubmit={submit} />)
  await userEvent.click(screen.getByRole('button', { name: /create pull request/i }))
  expect(submit).toHaveBeenCalled()
  expect(screen.getByText(/review expires/i)).toBeInTheDocument()
})
