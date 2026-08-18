import { expect, test } from '@playwright/test'

const inspection = { valid: true, uploadRevision: 'hash', preview: { name: 'sample', plugin: 'dotnet', disposition: 'update', description: 'sample' }, findings: [], normalizedAvailable: true }

test('update review groups operations and submits explicit confirmation', async ({ page }) => {
  await page.route('**/api/submissions/inspect', route => route.fulfill({ json: inspection }))
  await page.route('**/api/auth/session', route => route.fulfill({ json: { authenticated: true, githubUserId: 42, login: 'octocat' } }))
  await page.route('**/api/auth/csrf', route => route.fulfill({ json: { token: 'csrf' } }))
  await page.route('**/api/contributions/intents', route => route.fulfill({ status: 201, json: { id: 'update-1', contributionType: 'Update', targetRepository: 'dotnet/skills', destinationPath: 'plugins/dotnet/skills/sample', pullRequestTitle: 'Update sample skill', expiresAt: '2026-08-01T00:00:00Z', files: [
    { path: 'plugins/dotnet/skills/sample/add.txt', operation: 'add', sha256: 'a', size: 1 },
    { path: 'plugins/dotnet/skills/sample/SKILL.md', operation: 'change', sha256: 'b', size: 2 },
    { path: 'plugins/dotnet/skills/sample/old.txt', operation: 'delete', sha256: 'c', size: 3 },
  ] } }))
  await page.route('**/api/contributions/intents/update-1/submit', async route => {
    expect(route.request().headers()['x-confirm-update']).toBe('true')
    return route.fulfill({ status: 201, json: { id: 'contribution-update', state: 'PullRequestOpen', pullRequestUrl: 'https://github.com/dotnet/skills/pull/8', updatedAt: '2026-07-30T00:00:00Z' } })
  })

  await page.goto('/contribute/skill')
  await page.locator('input[type=file]').setInputFiles({ name: 'SKILL.md', mimeType: 'text/markdown', buffer: Buffer.from('skill') })
  await page.getByRole('button', { name: /prepare pull request/i }).click()
  await expect(page.getByText('Existing skill update')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Added files' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Changed files' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Removed files' })).toBeVisible()
  await page.getByRole('checkbox').check()
  await page.getByRole('button', { name: /create pull request/i }).click()
  await expect(page.getByRole('link', { name: /open pull request/i })).toBeVisible()
})

test('stale update conflict requires a fresh review and performs no success navigation', async ({ page }) => {
  await page.route('**/api/submissions/inspect', route => route.fulfill({ json: inspection }))
  await page.route('**/api/auth/session', route => route.fulfill({ json: { authenticated: true, githubUserId: 42, login: 'octocat' } }))
  await page.route('**/api/auth/csrf', route => route.fulfill({ json: { token: 'csrf' } }))
  await page.route('**/api/contributions/intents', route => route.fulfill({ status: 201, json: { id: 'update-stale', contributionType: 'Update', targetRepository: 'dotnet/skills', destinationPath: 'plugins/dotnet/skills/sample', pullRequestTitle: 'Update sample', expiresAt: '2026-08-01T00:00:00Z', files: [{ path: 'plugins/dotnet/skills/sample/SKILL.md', operation: 'change', sha256: 'b', size: 2 }] } }))
  await page.route('**/api/contributions/intents/update-stale/submit', route => route.fulfill({ status: 409, json: { category: 'conflict', message: 'The target repository changed after review.', nextAction: 'Refresh and review again.' } }))
  await page.goto('/contribute/skill')
  await page.locator('input[type=file]').setInputFiles({ name: 'SKILL.md', mimeType: 'text/markdown', buffer: Buffer.from('skill') })
  await page.getByRole('button', { name: /prepare pull request/i }).click()
  await page.getByRole('checkbox').check()
  await page.getByRole('button', { name: /create pull request/i }).click()
  await expect(page.getByText(/target repository changed/i)).toBeVisible()
  await expect(page.getByRole('link', { name: /open pull request/i })).toHaveCount(0)
})
