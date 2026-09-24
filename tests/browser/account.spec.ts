import { csrf, expect, expectConsoleError, test, totp } from './fixtures'

const openAccount = async (page: import('@playwright/test').Page) => {
  await page.locator('.side-nav').getByRole('button', { name: 'Minha conta' }).click()
  return page.getByRole('dialog', { name: 'Minha conta' })
}

test('changing the password signs out the other devices', async ({ page, account, browser }, testInfo) => {
  expectConsoleError('400')
  const phone = await browser.newContext({ baseURL: testInfo.project.use.baseURL })
  const login = await phone.request.post('/api/auth/login', { data: { email: account.email, password: account.password }, headers: csrf })
  expect(login.ok()).toBeTruthy()

  await page.goto('/')
  const dialog = await openAccount(page)
  await dialog.locator('input[name=current]').fill('Senha-errada-123')
  await dialog.locator('input[name=next]').fill('Nova-senha-456')
  await dialog.locator('input[name=confirm]').fill('Nova-senha-456')
  await dialog.getByRole('button', { name: 'Alterar senha' }).click()
  await expect(dialog.getByRole('alert')).toContainText('A senha atual está incorreta.')

  await dialog.locator('input[name=current]').fill(account.password)
  await dialog.getByRole('button', { name: 'Alterar senha' }).click()
  await expect(dialog.getByRole('status')).toContainText('Outro dispositivo foi desconectado')

  const phonePage = await phone.newPage()
  await phonePage.goto('/')
  await expect(phonePage.getByRole('heading', { name: 'Bem-vindo de volta' })).toBeVisible()
  await phone.close()
})

test('two-step verification protects the login and a recovery code turns it off', async ({ page, account }) => {
  expectConsoleError('401')
  await page.goto('/')
  let dialog = await openAccount(page)
  await dialog.getByRole('tab', { name: 'Duas etapas' }).click()
  await dialog.getByRole('button', { name: 'Configurar' }).click()
  await expect(dialog.getByRole('img', { name: /Código QR/ })).toBeVisible()
  const secret = await dialog.locator('.secret-key').innerText()
  await dialog.locator('input[name=code]').fill(totp(secret))
  await dialog.locator('input[name=password]').fill(account.password)
  await dialog.getByRole('button', { name: 'Ativar' }).click()

  const codes = dialog.locator('.recovery-grid code')
  await expect(codes).toHaveCount(10)
  const recovery = await codes.first().innerText()
  await dialog.getByRole('button', { name: 'Já guardei' }).click()
  await dialog.getByRole('button', { name: 'Fechar' }).click()
  await expect(page.locator('.stat-card', { hasText: 'SEGURANÇA' })).toContainText('Duas etapas ativa')

  await page.locator('.sidebar').getByRole('button', { name: 'Sair' }).click()
  const form = page.locator('form.auth-card')
  await form.locator('input[name=email]').fill(account.email)
  await form.locator('input[name=password]').fill(account.password)
  await form.locator('button.submit-button').click()
  await expect(page.getByRole('heading', { name: 'Digite o código' })).toBeVisible()
  // The code used to enable is spent; the next time step is still accepted.
  await form.locator('input[name=code]').fill(totp(secret, 1))
  await form.locator('button.submit-button').click()
  await expect(page.getByRole('heading', { name: /Olá, Pessoa/ })).toBeVisible()

  dialog = await openAccount(page)
  await dialog.getByRole('tab', { name: 'Duas etapas' }).click()
  const disable = dialog.locator('form', { hasText: 'Desativar' })
  await disable.locator('input[name=password]').fill(account.password)
  await disable.locator('input[name=code]').fill(recovery)
  await disable.getByRole('button', { name: 'Desativar duas etapas' }).click()
  await expect(dialog.getByRole('status')).toContainText('Verificação em duas etapas desativada.')
})

test('sessions and security events are listed', async ({ page, account }) => {
  await page.goto('/')
  const dialog = await openAccount(page)

  await dialog.getByRole('tab', { name: 'Sessões' }).click()
  await expect(dialog.locator('.session-row', { hasText: 'ESTE DISPOSITIVO' })).toHaveCount(1)

  await dialog.getByRole('tab', { name: 'Atividade' }).click()
  await expect(dialog).toContainText('Nenhuma tentativa com senha incorreta')
  await expect(dialog.locator('.event-list')).toContainText('Conta criada')
})
