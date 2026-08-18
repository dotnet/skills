import { expect, test } from '@playwright/test'

const inspection = { valid: true, uploadRevision: 'hash', preview: { name: 'sample', plugin: 'dotnet', disposition: 'new', description: 'sample' }, findings: [], normalizedAvailable: true }
const intent = { id: 'intent-1', contributionType: 'NewSkill', targetRepository: 'dotnet/skills', destinationPath: 'plugins/dotnet/skills/sample', pullRequestTitle: 'Contribute sample skill', expiresAt: '2026-08-01T00:00:00Z', files: [{ path: 'plugins/dotnet/skills/sample/SKILL.md', operation: 'add', sha256: 'hash', size: 10 }] }

test('popup sign-in preserves upload through review, duplicate-safe submit, and PR navigation', async ({ page }) => {
  let authenticated = false
  let submissions = 0
  await page.route('**/api/submissions/inspect', route => route.fulfill({ json: inspection }))
  await page.route('**/api/auth/session', route => route.fulfill({ json: authenticated ? { authenticated: true, githubUserId: 42, login: 'octocat' } : { authenticated: false } }))
  await page.route('**/api/auth/github/start', route => { authenticated = true; return route.fulfill({ json: { authorizationUrl: 'http://127.0.0.1:5173/auth-test', transactionId: 'tx', expiresAt: '2026-08-01T00:00:00Z' } }) })
  await page.route('**/auth-test', route => {
    authenticated = true
    return route.fulfill({ contentType: 'text/html', body: `<script>opener.postMessage({type:'skillcatalog:github-auth-complete'},location.origin);close()</script>` })
  })
  await page.route('**/api/auth/csrf', route => route.fulfill({ json: { token: 'csrf' } }))
  await page.route('**/api/contributions/intents', route => route.fulfill({ status: 201, json: intent }))
  await page.route('**/api/contributions/intents/intent-1/submit', route => { submissions++; return route.fulfill({ status: submissions === 1 ? 201 : 200, json: { id: 'contribution-1', state: 'PullRequestOpen', pullRequestUrl: 'https://github.com/dotnet/skills/pull/7', updatedAt: '2026-07-30T00:00:00Z' } }) })

  await page.goto('/contribute/skill')
  await page.locator('input[type=file]').setInputFiles({ name: 'SKILL.md', mimeType: 'text/markdown', buffer: Buffer.from('skill') })
  await expect(page.getByRole('heading', { name: 'sample' })).toBeVisible()
  const popupPromise = page.waitForEvent('popup')
  await page.getByRole('button', { name: /sign in with github/i }).click()
  await popupPromise
  await page.evaluate(() => window.dispatchEvent(new MessageEvent('message', { origin: window.location.origin, data: { type: 'skillcatalog:github-auth-complete' } })))
  await expect(page.getByText(/signed in as/i)).toContainText('octocat')
  await page.getByRole('button', { name: /prepare pull request/i }).click()
  await expect(page.getByText('plugins/dotnet/skills/sample', { exact: true })).toBeVisible()
  await page.getByRole('checkbox').check()
  await page.getByRole('button', { name: /create pull request/i }).click()
  await expect(page.getByRole('link', { name: /open pull request/i })).toHaveAttribute('href', 'https://github.com/dotnet/skills/pull/7')
  await expect(page.getByRole('link', { name: /view contribution progress/i })).toBeVisible()
  expect(submissions).toBe(1)

  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
