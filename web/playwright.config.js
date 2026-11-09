import { defineConfig, devices } from '@playwright/test';
const webkit = process.env.RITMO_TEST_BROWSER === 'webkit';
export default defineConfig({
  testDir: './tests', timeout: 45000, workers: 1,
  use: { baseURL: 'http://127.0.0.1:8107', ...(webkit ? devices['iPhone 13'] : { channel: 'msedge' }), headless: true },
  webServer: { command: 'node tests/server.mjs', url: 'http://127.0.0.1:8107', reuseExistingServer: false },
});
