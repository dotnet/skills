import { expect, test } from '@playwright/test'
test('search updates within one second and detail is usable within two', async ({ page }) => {
  await page.goto('/')
  const searchStart = Date.now()
  await page.getByRole('textbox', { name: /search skills/i }).fill('blazor')
  await expect(page.getByText('author-component', { exact: true })).toBeVisible({ timeout: 1000 })
  expect(Date.now() - searchStart).toBeLessThan(1000)
  const detailStart = Date.now()
  await page.getByRole('link', { name: /view skill/i }).first().click()
  await expect(page.getByRole('link', { name: /download skill/i })).toBeVisible({ timeout: 2000 })
  expect(Date.now() - detailStart).toBeLessThan(2000)
})
