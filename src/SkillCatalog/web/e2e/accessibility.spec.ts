import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'
for (const path of ['/', '/skills/dotnet/setup-local-sdk']) {
  test(`has no serious accessibility violations: ${path}`, async ({ page }) => {
    await page.goto(path)
    await expect(page.locator('main')).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter(x => ['serious','critical'].includes(x.impact ?? ''))).toEqual([])
  })
}
