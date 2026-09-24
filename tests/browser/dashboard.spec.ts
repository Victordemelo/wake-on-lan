import { createMachine, expect, expectConsoleError, test } from './fixtures'

test('a machine is added, woken, edited and removed while its history stays', async ({ page, account }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: new RegExp(`Olá, ${account.name.split(' ')[0]}`) })).toBeVisible()

  await page.getByRole('button', { name: 'Nova máquina' }).click()
  const dialog = page.locator('form.dialog')
  await dialog.getByLabel('Nome da máquina').fill('PC do escritório')
  await dialog.getByLabel('Endereço MAC').fill('aa-bb-cc-dd-ee-01')
  await dialog.getByLabel('Destino').fill('127.0.0.1')
  await dialog.getByRole('button', { name: 'Salvar máquina' }).click()
  await expect(page.locator('.toast')).toContainText('Máquina salva')
  const card = page.locator('.machine-card', { hasText: 'PC do escritório' })
  await expect(card).toContainText('AA:BB:CC:DD:EE:01')

  await card.getByRole('button', { name: 'Ligar máquina' }).click()
  await expect(page.locator('.toast')).toHaveText('Magic Packet enviado.')
  await expect(page.locator('.toast')).not.toHaveClass(/error/)

  await card.getByRole('button', { name: 'Editar PC do escritório' }).click()
  await expect(dialog.getByLabel('Endereço MAC')).toHaveValue('AA:BB:CC:DD:EE:01')
  await dialog.getByLabel('Nome da máquina').fill('PC da sala')
  await dialog.getByRole('button', { name: 'Salvar alterações' }).click()
  const renamed = page.locator('.machine-card', { hasText: 'PC da sala' })
  await expect(renamed).toBeVisible()

  page.once('dialog', (confirmation) => confirmation.accept())
  await renamed.getByRole('button', { name: 'Remover PC da sala' }).click()
  await expect(renamed).toHaveCount(0)
  // The history keeps the name the machine had when it was woken.
  await expect(page.locator('#activity')).toContainText('PC do escritório · Ligar')
})

test('a failed command is shown as an error', async ({ page, account }) => {
  expectConsoleError('400')
  await createMachine(page, `PC de ${account.name}`)
  await page.route('**/api/machines/*/wake', (route) => route.fulfill({
    status: 400,
    contentType: 'application/json',
    body: JSON.stringify({ succeeded: false, message: 'O gateway residencial está offline.' }),
  }))
  await page.goto('/')

  await page.getByRole('button', { name: 'Ligar máquina' }).click()

  const toast = page.locator('.toast')
  await expect(toast).toHaveClass(/error/)
  await expect(toast).toHaveAttribute('role', 'alert')
  await expect(toast).toContainText('O gateway residencial está offline.')
})

test('an invalid MAC address is explained in Portuguese', async ({ page, account }) => {
  expectConsoleError('400')
  await page.goto('/')
  await expect(page.getByRole('heading', { name: new RegExp(`Olá, ${account.name.split(' ')[0]}`) })).toBeVisible()

  await page.getByRole('button', { name: 'Nova máquina' }).click()
  const dialog = page.locator('form.dialog')
  await dialog.getByLabel('Nome da máquina').fill('PC inválido')
  await dialog.getByLabel('Endereço MAC').fill('AA:BB')
  await dialog.getByRole('button', { name: 'Salvar máquina' }).click()

  await expect(dialog.locator('.error')).toContainText('Informe um endereço MAC válido')
})
