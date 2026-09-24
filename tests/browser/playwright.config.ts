import { defineConfig, devices } from '@playwright/test'

// Runs against a disposable instance started by scripts/Test-Browser.ps1.
export default defineConfig({
  testDir: '.',
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  forbidOnly: !!process.env.CI,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://127.0.0.1:8005',
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    // Creates the first account with the setup code; every other test uses its own account.
    { name: 'first-account', testMatch: /first-account\.setup\.ts/ },
    { name: 'desktop', use: { ...devices['Desktop Chrome'] }, dependencies: ['first-account'], testIgnore: /mobile\.spec\.ts/ },
    { name: 'mobile', use: { ...devices['Pixel 7'] }, dependencies: ['first-account'], testMatch: /mobile\.spec\.ts/ },
  ],
})
