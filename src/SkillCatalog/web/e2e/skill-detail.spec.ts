import { expect, test } from '@playwright/test'
test('opens a stable, sanitized skill detail route', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('link', { name: /view skill/i }).first().click()
  await expect(page.locator('.detail-head > div > h1')).toBeVisible()
  await expect(page.getByRole('link', { name: /download skill/i })).toBeVisible()
  await expect(page.locator('.markdown script')).toHaveCount(0)
})
