import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests/e2e', workers: 1, timeout: 60000,
  use: { baseURL: process.env.TREINOS_TEST_URL || 'https://localhost:8443', ignoreHTTPSErrors: true, launchOptions: { args: ['--ignore-certificate-errors'] }, trace: 'retain-on-failure' },
  reporter: [['list']],
});
