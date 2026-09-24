import { expect, test } from './fixtures'

test('visitors see the project presentation and sign in on a page of its own', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toContainText('de qualquer lugar')
  await expect(page.locator('input[type=password]')).toHaveCount(0)
  await expect(page.locator('.lp-hero').getByRole('link', { name: 'Ver no GitHub' }))
    .toHaveAttribute('href', 'https://github.com/Victordemelo/wake-on-lan')
  await expect(page.locator('#android')).toContainText('Baixar APK')

  await page.locator('.lp-header').getByRole('link', { name: 'Entrar' }).click()
  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByRole('heading', { name: 'Bem-vindo de volta' })).toBeVisible()

  await page.goBack()
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('heading', { level: 1 })).toContainText('de qualquer lugar')
})

test('a signed-in person skips the presentation and the sign-in page', async ({ page, account }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: new RegExp(`Olá, ${account.name.split(' ')[0]}`) })).toBeVisible()
  await expect(page).toHaveURL(/\/$/)
})
