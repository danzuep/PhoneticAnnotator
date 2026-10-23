import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PHONETIC_BASE_URL || 'http://127.0.0.1:5187/PhoneticAnnotator/';

export default defineConfig({
    testDir: './tests',
    timeout: 120_000,
    expect: { timeout: 60_000 },
    use: { baseURL, trace: 'retain-on-failure' },
    outputDir: '../../artifacts/browser-results',
    reporter: [['list'], ['html', { outputFolder: '../../artifacts/browser-report', open: 'never' }]],
    projects: [
        { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
        { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
        { name: 'webkit', use: { ...devices['Desktop Safari'] } }
    ],
    webServer: process.env.PHONETIC_BASE_URL ? undefined : {
        command: 'dotnet serve --address 127.0.0.1 --port 5187 --directory ../../artifacts/site',
        url: baseURL,
        reuseExistingServer: !process.env.CI
    }
});
