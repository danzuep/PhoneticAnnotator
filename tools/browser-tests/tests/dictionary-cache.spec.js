import { test, expect } from '@playwright/test';

test('standalone published cache module revalidates, persists, handles offline and isolates clearing', async ({ page, context }) => {
    // Import the published module without starting either WebAssembly runtime.
    await page.route('**/PhoneticAnnotator/', route => route.fulfill({
        contentType: 'text/html', body: '<!doctype html><title>Dictionary cache regression</title>'
    }));
    await page.goto('./');
    const requests = [];
    let version = 'one';
    await page.route('**/cache-regression.tsv', route => {
        const headers = route.request().headers();
        requests.push(headers);
        return route.fulfill(headers['if-none-match'] === `"${version}"`
            ? { status: 304, body: '' }
            : { status: 200, body: version, headers: { ETag: `"${version}"` } });
    });
    const load = () => page.evaluate(async () => {
        const { loadBundledDictionary } = await import('./dictionary-cache.js');
        const result = await loadBundledDictionary('./cache-regression.tsv');
        return { source: result.source, text: new TextDecoder().decode(result.bytes) };
    });
    await page.evaluate(async () => {
        const { clearDictionaryCache } = await import('./dictionary-cache.js');
        await clearDictionaryCache();
        await (await caches.open('unrelated-cache-regression')).put('./unrelated', new Response('keep'));
    });
    expect(await load()).toEqual({ source: 'network', text: 'one' });
    await page.reload();
    expect(await load()).toEqual({ source: 'cache', text: 'one' });
    expect(requests[1]['if-none-match']).toBe('"one"');
    version = 'two';
    expect(await load()).toEqual({ source: 'network', text: 'two' });
    await page.route('**/abort-regression.tsv', async route => {
        await page.evaluate(() => globalThis.cacheTestAbort.abort());
        await route.abort();
    });
    expect(await page.evaluate(async () => {
        const { loadDictionary, DICTIONARY_CACHE_NAME } = await import('./dictionary-cache.js');
        const cache = await caches.open(DICTIONARY_CACHE_NAME);
        await cache.put('./abort-regression.tsv', new Response('old', { headers: { ETag: '"old"' } }));
        const controller = globalThis.cacheTestAbort = new AbortController();
        try {
            await loadDictionary('./abort-regression.tsv', { signal: controller.signal });
            return 'unexpected success';
        } catch (error) {
            return error.name;
        } finally {
            delete globalThis.cacheTestAbort;
        }
    })).toBe('AbortError');
    await page.unroute('**/cache-regression.tsv');
    await context.setOffline(true);
    try {
        expect(await load()).toEqual({ source: 'offline-cache', text: 'two' });
    } finally {
        await context.setOffline(false);
    }
    await page.route('**/cache-regression.tsv', route => route.fulfill({ status: 500, body: 'error' }));
    expect(await page.evaluate(async () => {
        const { loadBundledDictionary } = await import('./dictionary-cache.js');
        try { await loadBundledDictionary('./cache-regression.tsv'); return 'unexpected success'; }
        catch (error) { return error.message; }
    })).toContain('500');
    expect(await page.evaluate(async () => {
        const { clearDictionaryCache, DICTIONARY_CACHE_NAME } = await import('./dictionary-cache.js');
        await clearDictionaryCache();
        const names = await caches.keys();
        const result = { app: names.includes(DICTIONARY_CACHE_NAME), other: names.includes('unrelated-cache-regression') };
        await caches.delete('unrelated-cache-regression');
        return result;
    })).toEqual({ app: false, other: true });
});
