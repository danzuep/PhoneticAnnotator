import { test, expect } from '@playwright/test';
import { writeFile } from 'node:fs/promises';

test('local .NET worker profiling emits measured JSON, not a latency budget', async ({ page }, testInfo) => {
    await page.goto('./profile.html');
    const result = await page.evaluate(async buildLabel => {
        const { runProfile } = await import('./profile.js');
        return runProfile({ iterations: 3, warmups: 1, timeoutMs: 120000, buildLabel });
    }, process.env.PHONETIC_PROFILE_LABEL || `${testInfo.project.name} / unspecified build`);
    result.automation = {
        project: testInfo.project.name,
        browserName: testInfo.project.use.browserName,
        mobileEmulation: Boolean(testInfo.project.use.isMobile),
        physicalDeviceMeasurement: false
    };
    const resultPath = testInfo.outputPath('wasm-profile.json');
    await writeFile(resultPath, JSON.stringify(result, null, 2));
    await testInfo.attach('wasm-profile.json', { path: resultPath, contentType: 'application/json' });
    expect(result.dictionaryCacheClearSucceeded).toBe(true);
    expect(result.passes.map(pass => pass.cacheState)).toEqual(['cold-dictionary-cache', 'warm-dictionary-cache']);
    for (const dictionary of ['word', 'unihan']) {
        expect(result.passes[0].dictionaries[dictionary].source).toBe('network');
        // Validators/storage availability vary by host: never mislabel a warm network response as a cache hit.
        expect(['network', 'cache', 'offline-cache']).toContain(result.passes[1].dictionaries[dictionary].source);
        expect(result.passes[1].dictionaries[dictionary].fingerprint)
            .toBe(result.passes[0].dictionaries[dictionary].fingerprint);
    }
    for (const pass of result.passes) {
        expect(Number.isFinite(pass.runtimeInitializeMs)).toBe(true);
        expect(pass.dictionaries.word.bytes).toBeGreaterThan(0);
        expect(pass.dictionaries.unihan.bytes).toBeGreaterThan(0);
        expect(pass.unihanIndexes.map(index => index.readingSystem)).toEqual(['kJapanese', 'kMandarin', 'kHangul']);
        expect(pass.workloads).toHaveLength(21);
        for (const workload of pass.workloads) {
            expect(workload.samplesMs).toHaveLength(3);
            expect(workload.samplesMs.every(value => Number.isFinite(value) && value >= 0)).toBe(true);
            expect(workload.annotatedNodes).toBeGreaterThan(0);
            expect(workload).not.toHaveProperty('seed');
            expect(workload).not.toHaveProperty('text');
            expect(workload).not.toHaveProperty('reading');
        }
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('bounded options, statistics and worker failure cleanup', async ({ page }) => {
    await page.goto('./profile.html');
    const result = await page.evaluate(async () => {
        const { validateOptions, summarize, createWorkerClient } = await import('./profile.js');
        const invalid = [
            null, [], { iterations: 0 }, { iterations: 51 }, { iterations: 1.2 }, { iterations: '3' },
            { warmups: -1 }, { warmups: 11 }, { timeoutMs: 999 }, { timeoutMs: 300001 },
            { buildLabel: 'x'.repeat(101) }, { buildLabel: 1 }, { unknown: true }
        ].map(options => {
            try { validateOptions(options); return false; } catch { return true; }
        });
        const OriginalWorker = window.Worker;
        const outcomes = [];
        try {
            for (const mode of ['error', 'messageerror', 'startup-error', 'timeout', 'post-error']) {
                let terminated = false;
                window.Worker = class {
                    postMessage() {
                        if (mode === 'post-error') throw new Error('post failed');
                        queueMicrotask(() => {
                            if (mode === 'error') this.onerror({ preventDefault() {}, message: 'worker crashed' });
                            if (mode === 'messageerror') this.onmessageerror();
                            if (mode === 'startup-error') this.onmessage({ data: { type: mode, error: { message: 'startup failed' } } });
                        });
                    }
                    terminate() { terminated = true; }
                };
                // Internal client timeout is kept small in this deterministic error-path test only.
                const client = createWorkerClient(10);
                const pending = await Promise.allSettled([client.call('initialize'), client.call('AnnotateToJson')]);
                const sticky = await client.call('initialize').then(() => false, () => true);
                outcomes.push({ rejected: pending.every(item => item.status === 'rejected'), sticky, terminated });
                client.close();
            }
        } finally {
            window.Worker = OriginalWorker;
        }
        return { invalid, outcomes, stats: summarize([4, 1, 3, 2], 100) };
    });
    expect(result.invalid.every(Boolean)).toBe(true);
    expect(result.outcomes).toEqual(Array(5).fill({ rejected: true, sticky: true, terminated: true }));
    expect(result.stats).toEqual({ samplesMs: [4, 1, 3, 2], medianMs: 2.5, p95Ms: 4, charsPerSecond: 40000 });
});

test('standalone page stays idle and reports unavailable cache accessibly', async ({ page }) => {
    const runtimeRequests = [];
    page.on('request', request => {
        if (/\/_framework\/|phonetic-annotator-worker\.js/.test(request.url())) runtimeRequests.push(request.url());
    });
    await page.route('**/dictionary-cache.js', route => route.fulfill({
        contentType: 'text/javascript',
        body: 'export const clearDictionaryCache = async () => false; export const loadBundledDictionary = async () => { throw new Error("unexpected download"); };'
    }));
    await page.goto('./profile.html');
    await expect(page.getByRole('status')).toHaveText('Ready. No runtime has started.');
    expect(runtimeRequests).toEqual([]);
    await page.getByRole('button', { name: 'Run profile (clears dictionary cache)' }).click();
    await expect(page.getByRole('alert')).toContainText('cold/warm cache comparison is unavailable');
    await expect(page.locator('#profile-controls')).toBeEnabled();
    await expect(page.getByRole('button', { name: 'Download local JSON' })).toBeDisabled();
    expect(runtimeRequests).toEqual([]);
});

test('dictionary timeout aborts its fetch and terminates the profiling worker', async ({ page }) => {
    await page.route('**/dictionary-cache.js', route => route.fulfill({
        contentType: 'text/javascript',
        body: `
            export const clearDictionaryCache = async () => true;
            export const loadBundledDictionary = (_url, { signal }) => new Promise((_, reject) => {
                signal.addEventListener('abort', () => {
                    globalThis.profileDictionaryAborted = true;
                    reject(signal.reason);
                }, { once: true });
            });
        `
    }));
    await page.goto('./profile.html');
    const outcome = await page.evaluate(async () => {
        const original = window.Worker;
        let terminated = false;
        window.Worker = class {
            postMessage({ id }) {
                queueMicrotask(() => this.onmessage({ data: { id, ok: true } }));
            }
            terminate() { terminated = true; }
        };
        try {
            const { runProfile } = await import('./profile.js');
            try {
                await runProfile({ iterations: 1, warmups: 0, timeoutMs: 1000 });
                return { unexpectedSuccess: true };
            } catch (error) {
                return { message: error.message, aborted: globalThis.profileDictionaryAborted, terminated };
            }
        } finally {
            window.Worker = original;
        }
    });
    expect(outcome).toEqual({ message: 'word dictionary load timed out.', aborted: true, terminated: true });
});
