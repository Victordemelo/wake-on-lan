import { createMachine, csrf, expect, expectConsoleError, test } from './fixtures'

test('without a connection the app says so and recovers', async ({ page }) => {
  expectConsoleError('ERR_FAILED')
  await page.route('**/api/auth/session', (route) => route.abort())
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Sem conexão com o servidor' })).toBeVisible()

  await page.unroute('**/api/auth/session')
  await page.getByRole('button', { name: 'Tentar novamente' }).click()

  await expect(page.locator('form.auth-card')).toBeVisible()
})

test('a session ended elsewhere hides the account data at once', async ({ page, account }) => {
  expectConsoleError('401')
  await createMachine(page, `PC de ${account.name}`)
  await page.goto('/')
  await expect(page.locator('.machine-card')).toHaveCount(1)

  const sessions: { id: string; current: boolean }[] = await (await page.request.get('/api/account/sessions')).json()
  const current = sessions.find((session) => session.current)!
  await page.request.delete(`/api/account/sessions/${current.id}`, { headers: csrf })
  await page.getByRole('button', { name: 'Ligar máquina' }).click()

  await expect(page.locator('form.auth-card')).toBeVisible()
  await expect(page.locator('.machine-card')).toHaveCount(0)
})
