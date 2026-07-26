import { expect, test } from '@playwright/test'
for (const width of [320, 768, 1440]) {
  test(`core journey fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await page.goto('/')
    await page.keyboard.press('Tab')
    await expect(page.getByRole('link', { name: /skip to content/i })).toBeFocused()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
    expect(overflow).toBe(false)
  })
}
