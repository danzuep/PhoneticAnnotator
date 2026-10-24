import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PHONETIC_BASE_URL || 'http://127.0.0.1:5187/PhoneticAnnotator/';

export default defineConfig({
    testDir: './profiling',
    timeout: 600_000,
    workers: 1,
    fullyParallel: false,
    retries: 0,
    use: { baseURL, trace: 'retain-on-failure' },
    outputDir: '../../artifacts/profiling-results',
    reporter: [['list'], ['html', { outputFolder: '../../artifacts/profiling-report', open: 'never' }]],
    projects: [
        { name: 'chromium', use: { ...devices['Desktop Chrome'], channel: 'chromium-headless-shell' } },
        {
            name: 'mobile-chromium-emulated',
            use: { ...devices['Pixel 7'], channel: 'chromium-headless-shell' }
        },
        // Optional installed real WebKit engine, not Chromium with a Safari user-agent.
        ...(process.env.PHONETIC_PROFILE_WEBKIT === '1'
            ? [{ name: 'webkit', use: { ...devices['Desktop Safari'] } }] : [])
    ],
    webServer: process.env.PHONETIC_BASE_URL ? undefined : {
        command: 'dotnet serve --address 127.0.0.1 --port 5187 --directory ../../artifacts/site',
        url: baseURL,
        reuseExistingServer: !process.env.CI
    }
});
