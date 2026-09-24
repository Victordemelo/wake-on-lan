import { expect, expectConsoleError, owner, setupToken, test as setup } from './fixtures'

setup('the first account needs the setup code from the API log', async ({ page }) => {
  const status = await (await page.request.get('/api/auth/registration')).json()
  setup.skip(!status.setupRequired, 'A primeira conta já existe nesta instância.')
  expect(setupToken, 'defina E2E_SETUP_TOKEN').not.toBe('')
  expectConsoleError('403')

  await page.goto('/')
  await page.locator('.lp-header').getByRole('link', { name: 'Entrar' }).click()
  await expect(page.getByRole('heading', { name: 'Crie a primeira conta' })).toBeVisible()
  const form = page.locator('form.auth-card')
  await form.locator('input[name=setupToken]').fill('CODIGO-ERRADO')
  await form.locator('input[name=name]').fill('Dona da casa')
  await form.locator('input[name=email]').fill(owner.email)
  await form.locator('input[name=password]').fill(owner.password)
  await form.locator('button.submit-button').click()
  await expect(form.getByRole('alert')).toContainText('Código de configuração inválido')

  await form.locator('input[name=setupToken]').fill(setupToken)
  await form.locator('button.submit-button').click()
  await expect(page.getByRole('heading', { name: /Olá, Dona/ })).toBeVisible()
})
