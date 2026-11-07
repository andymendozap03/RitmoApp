import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests', timeout: 45000, workers: 1,
  use: { baseURL: 'http://127.0.0.1:8107', channel: 'msedge', headless: true },
  webServer: { command: 'npx vite preview --host 127.0.0.1 --port 8107', url: 'http://127.0.0.1:8107', reuseExistingServer: false },
});
