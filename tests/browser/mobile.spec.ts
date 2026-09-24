import { createMachine, expect, test } from './fixtures'

const horizontalOverflow = (page: import('@playwright/test').Page) =>
  page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)

test('the presentation page fits a phone screen', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toContainText('de qualquer lugar')
  await expect(page.locator('.lp-header').getByRole('link', { name: 'Entrar' })).toBeVisible()

  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
})

test('the sign-in page fits a phone screen', async ({ page }) => {
  await page.goto('/login')
  await expect(page.locator('form.auth-card')).toBeVisible()

  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
})

test('the panel and the account dialog fit a phone screen', async ({ page, account }) => {
  await createMachine(page, `PC de ${account.name}`)
  await page.goto('/')
  await expect(page.locator('.machine-card')).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)

  await page.locator('.mobile-topbar').getByRole('button', { name: 'Minha conta' }).click()

  const dialog = page.getByRole('dialog', { name: 'Minha conta' })
  await expect(dialog).toBeVisible()
  await dialog.getByRole('tab', { name: 'Sessões' }).click()
  await expect(dialog.locator('.session-row')).toHaveCount(1)
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
})
