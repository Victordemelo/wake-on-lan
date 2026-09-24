import { createHmac, randomUUID } from 'node:crypto'
import { expect, test as base, type Page } from '@playwright/test'

export { expect }

// Account created by first-account.setup.ts; the gateway of the test instance serves it.
export const owner = {
  email: process.env.E2E_OWNER_EMAIL ?? 'dona@e2e.test',
  password: process.env.E2E_OWNER_PASSWORD ?? 'Senha-da-dona-123',
}
export const setupToken = process.env.E2E_SETUP_TOKEN ?? ''
export const csrf = { 'X-Remote-Wake-Request': '1' }

export type Account = { name: string; email: string; password: string }

type Fixtures = {
  consoleGuard: void
  account: Account
}

export const test = base.extend<Fixtures>({
  // Any browser console error fails the test (CSP violations, script errors, failed
  // requests), except those a test declares with expectConsoleError().
  consoleGuard: [async ({ page }, use, testInfo) => {
    const errors: string[] = []
    page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()) })
    page.on('pageerror', (error) => errors.push(error.message))
    await use()
    const expected = testInfo.annotations
      .filter((annotation) => annotation.type === 'expected-console-error')
      .map((annotation) => annotation.description ?? '')
    expect(errors.filter((text) => !expected.some((part) => text.includes(part))), 'erros no console do navegador').toEqual([])
  }, { auto: true }],

  // A new account per test, registered through the API: the page context keeps its session cookie.
  account: async ({ page }, use) => {
    const account = { name: 'Pessoa de teste', email: `pessoa-${randomUUID()}@e2e.test`, password: 'Senha-forte-123' }
    const response = await page.request.post('/api/auth/register', { data: account, headers: csrf })
    expect(response.ok(), await response.text()).toBeTruthy()
    await use(account)
  },
})

export function expectConsoleError(part: string) {
  test.info().annotations.push({ type: 'expected-console-error', description: part })
}

export async function signIn(page: Page, email: string, password: string) {
  await page.goto('/')
  const form = page.locator('form.auth-card')
  await form.locator('input[name=email]').fill(email)
  await form.locator('input[name=password]').fill(password)
  await form.locator('button.submit-button').click()
}

export async function createMachine(page: Page, name: string, method = 'LocalBroadcast') {
  const response = await page.request.post('/api/machines', {
    headers: csrf,
    data: { name, macAddress: '02:00:00:00:00:01', hostname: 'pc-teste', broadcastAddress: '127.0.0.1', wolPort: 9, wakeMethod: method },
  })
  expect(response.status(), await response.text()).toBe(201)
}

export function uniqueName(prefix: string) {
  return `${prefix} ${randomUUID().slice(0, 6)}`
}

// RFC 6238 code for the base32 secret shown in the two-step setup, like an authenticator app.
export function totp(secret: string, stepOffset = 0) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  const bytes: number[] = []
  let buffer = 0
  let bits = 0
  for (const character of secret.replace(/\s+/g, '').toUpperCase()) {
    buffer = ((buffer << 5) | alphabet.indexOf(character)) & 0xffff
    bits += 5
    if (bits >= 8) {
      bytes.push((buffer >>> (bits - 8)) & 0xff)
      bits -= 8
    }
  }
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000) + stepOffset))
  const hash = createHmac('sha1', Buffer.from(bytes)).update(counter).digest()
  const offset = hash[hash.length - 1] & 0x0f
  const value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3]
  return String(value % 1_000_000).padStart(6, '0')
}
