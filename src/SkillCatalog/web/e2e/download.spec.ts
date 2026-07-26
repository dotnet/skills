import { expect, test } from '@playwright/test'
test('downloads an isolated skill archive', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('link', { name: /view skill/i }).first().click()
  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('link', { name: /download skill/i }).click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toMatch(/\.zip$/)
})
