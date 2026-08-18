import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

for (const state of ['ChecksPending', 'AwaitingReview', 'Merged', 'Closed', 'RecoveryRequired'] as const) {
  test(`renders authoritative ${state} status with evidence and recovery`, async ({ page }) => {
    await page.route('**/api/contributions/contribution-1**', route => route.fulfill({ json: {
      id: 'contribution-1', state, pullRequestUrl: 'https://github.com/dotnet/skills/pull/7', updatedAt: '2026-07-30T00:00:00Z', lastReconciledAt: '2026-07-30T00:00:00Z',
      recoveryMessage: state === 'RecoveryRequired' ? 'Inspect the fork before retrying.' : undefined,
      evidence: [{ kind: 'check', label: 'Build', status: state === 'ChecksPending' ? 'in_progress' : 'success', url: 'https://checks/7' }]
    } }))
    await page.goto('/contributions/contribution-1')
    await expect(page.getByText(/current state:/i)).toBeVisible()
    await expect(page.getByRole('link', { name: 'Build', exact: true })).toBeVisible()
    if (state === 'RecoveryRequired') await expect(page.getByText(/inspect the fork/i)).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter(issue => ['serious', 'critical'].includes(issue.impact ?? ''))).toEqual([])
  })
}

test('manual refresh is the polling fallback', async ({ page }) => {
  let reads = 0
  await page.route('**/api/contributions/contribution-1**', route => { reads++; return route.fulfill({ json: { id: 'contribution-1', state: reads > 1 ? 'AwaitingReview' : 'ChecksPending', updatedAt: '2026-07-30T00:00:00Z', lastReconciledAt: '2026-07-30T00:00:00Z' } }) })
  await page.goto('/contributions/contribution-1')
  await page.getByRole('button', { name: /refresh from github/i }).click()
  await expect(page.getByText(/current state:/i)).toBeVisible()
  expect(reads).toBeGreaterThan(1)
})
