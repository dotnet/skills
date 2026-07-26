import { expect, test } from '@playwright/test'
test('browse, search, and filter repository skills', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: /find the right skill/i })).toBeVisible()
  await expect(page.getByText(/98 skills/)).toBeVisible()
  await page.getByRole('textbox', { name: /search skills/i }).fill('blazor')
  await expect(page.getByText(/skills$/).first()).toBeVisible()
  await expect(page.getByText('author-component', { exact: true })).toBeVisible()
})
