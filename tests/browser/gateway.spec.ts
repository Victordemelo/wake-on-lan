import { expect, owner, signIn, test, uniqueName } from './fixtures'

// The test instance runs the demo gateway, which only sends Magic Packets to its own loopback.
test('the gateway owner wakes a machine through the home gateway', async ({ page }) => {
  await signIn(page, owner.email, owner.password)
  const gateway = page.locator('.stat-card', { hasText: 'GATEWAY' })
  await expect(gateway).toContainText('Online', { timeout: 40_000 })

  const name = uniqueName('PC pelo gateway')
  await page.getByRole('button', { name: 'Nova máquina' }).click()
  const dialog = page.locator('form.dialog')
  await dialog.getByLabel('Nome da máquina').fill(name)
  await dialog.getByLabel('Endereço MAC').fill('02:00:00:00:00:02')
  await dialog.getByLabel('Método de ativação').selectOption('TailscaleGateway')
  await dialog.getByLabel('Destino').fill('127.0.0.1')
  await dialog.getByRole('button', { name: 'Salvar máquina' }).click()

  const card = page.locator('.machine-card', { hasText: name })
  await expect(card).toContainText('Gateway residencial')
  await card.getByRole('button', { name: 'Ligar máquina' }).click()
  await expect(page.locator('.toast')).toHaveText('Magic Packet enviado pelo gateway residencial.')
})

test('other accounts see the gateway as not configured', async ({ page, account }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: new RegExp(`Olá, ${account.name.split(' ')[0]}`) })).toBeVisible()

  await expect(page.locator('.stat-card', { hasText: 'GATEWAY' })).toContainText('Não configurado')
})
