import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';

/**
 * Runs the ASP.NET Core app locally in Development mode (dev-auth: every request is an Admin
 * called "Dev User") against a throw-away SQLite database, then drives it with Chromium.
 *
 * Set E2E_BASE_URL to test an already-running instance instead (no web server is started).
 */
const baseURL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5199';
const repoRoot = path.resolve(__dirname, '..');
const dataDir = path.join(__dirname, '.e2e-data');

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  forbidOnly: !!process.env.CI,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    ...devices['Desktop Chrome'],
    viewport: { width: 1366, height: 900 },
    // Guided tours auto-start once per browser; mark them seen so they never cover the page mid-test.
    // help.spec.ts opens its own fresh context to test the first-run behaviour.
    storageState: {
      cookies: [],
      origins: [{ origin: baseURL, localStorage: ['welcome', 'details'].map(t => ({ name: `is-tour-seen:${t}`, value: '1' })) }]
    }
  },
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: [
          'mkdir -p .e2e-data && rm -f .e2e-data/e2e.db*',
          `dotnet run --project ${path.join(repoRoot, 'src/InitiativeScoping.Web')} --no-launch-profile -c Release`
        ].join(' && '),
        url: `${baseURL}/health`,
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
        stdout: 'ignore',
        stderr: 'pipe',
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_URLS: baseURL,
          ConnectionStrings__Default: `Data Source=${path.join(dataDir, 'e2e.db')}`,
          Auth__UseDevelopmentAuth: 'true',
          Auth__Dev__Roles__0: 'Admin',
          Serilog__MinimumLevel__Default: 'Warning'
        }
      }
});
