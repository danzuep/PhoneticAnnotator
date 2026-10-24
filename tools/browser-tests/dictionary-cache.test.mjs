import test from 'node:test';
import assert from 'node:assert/strict';
import { createDictionaryCache, DICTIONARY_CACHE_NAME } from '../../src/PhoneticAnnotator.Wasm/demo/dictionary-cache.js';

const baseURL = 'https://example.test/PhoneticAnnotator/';
const url = `${baseURL}word-readings.tsv`;
const text = result => new TextDecoder().decode(result.bytes);
const response = (body = 'old', headers = { ETag: '"v1"' }) => new Response(body, { headers });
function storage() {
    const caches = new Map();
    return {
        caches,
        async open(name) {
            if (!caches.has(name)) caches.set(name, new Map());
            const entries = caches.get(name);
            return {
                async match(key) { return entries.get(key)?.clone(); },
                async put(key, value) { entries.set(key, value.clone()); }
            };
        },
        async delete(name) { return caches.delete(name); }
    };
}
async function setup(fetch, cached = response()) {
    const cacheStorage = storage();
    if (cached) await (await cacheStorage.open(DICTIONARY_CACHE_NAME)).put(url, cached);
    return { cacheStorage, client: createDictionaryCache({ baseURL, cacheStorage, fetch }) };
}

test('cold download persists and later 304 revalidates ETag and Last-Modified', async () => {
    const cacheStorage = storage();
    const client = createDictionaryCache({
        baseURL, cacheStorage,
        fetch: async () => response('dictionary', { ETag: '"v1"', 'Last-Modified': 'yesterday' })
    });
    assert.equal((await client.load(url)).source, 'network');
    const another = createDictionaryCache({
        baseURL, cacheStorage,
        fetch: async (_, options) => {
            assert.equal(options.headers.get('If-None-Match'), '"v1"');
            assert.equal(options.headers.get('If-Modified-Since'), 'yesterday');
            assert.equal(options.cache, 'no-store');
            assert.equal(options.mode, 'same-origin');
            return new Response(null, { status: 304 });
        }
    });
    const result = await another.load(url);
    assert.equal(result.source, 'cache');
    assert.equal(text(result), 'dictionary');
});

test('changed 200 replaces persistent bytes and validators', async () => {
    const { client, cacheStorage } = await setup(async () => response('new', { ETag: '"v2"' }));
    assert.equal(text(await client.load(url)), 'new');
    const cached = await (await cacheStorage.open(DICTIONARY_CACHE_NAME)).match(url);
    assert.equal(await cached.text(), 'new');
    assert.equal(cached.headers.get('ETag'), '"v2"');
});

test('Last-Modified alone supports revalidation', async () => {
    const { client } = await setup(async (_, { headers }) => {
        assert.equal(headers.get('If-Modified-Since'), 'yesterday');
        return new Response(null, { status: 304 });
    }, response('old', { 'Last-Modified': 'yesterday' }));
    assert.equal((await client.load(url)).source, 'cache');
});

test('network failure returns explicitly offline bytes; cold network failure rejects', async () => {
    const fetch = async () => { throw new TypeError('offline'); };
    const { client } = await setup(fetch);
    const result = await client.load(url);
    assert.equal(result.source, 'offline-cache');
    assert.equal(text(result), 'old');
    await assert.rejects((await setup(fetch, null)).client.load(url), /offline/);
});

test('no validators means unconditional refetch on every load, never unsolicited 304', async () => {
    let requests = 0;
    const { client } = await setup(async (_, { headers }) => {
        assert.deepEqual([...headers], []);
        return response(`new${++requests}`, {});
    }, response('old', {}));
    assert.equal(text(await client.load(url)), 'new1');
    assert.equal(text(await client.load(url)), 'new2');
    const invalid = await setup(async () => new Response(null, { status: 304 }), response('old', {}));
    await assert.rejects(invalid.client.load(url), /304/);
});

test('HTTP errors never become stale success or overwrite cache', async () => {
    for (const status of [404, 500]) {
        const { client, cacheStorage } = await setup(async () => new Response('error', { status }));
        await assert.rejects(client.load(url), new RegExp(String(status)));
        const cached = await (await cacheStorage.open(DICTIONARY_CACHE_NAME)).match(url);
        assert.equal(await cached.text(), 'old');
    }
});

test('storage denial, corrupt reads and quota failures preserve successful network loads', async () => {
    for (const cacheStorage of [
        undefined,
        { async open() { throw new Error('denied'); } },
        { async open() { return { async match() { throw new Error('corrupt'); } }; } },
        { async open() { return { async match() {
            return { ok: true, async arrayBuffer() { throw new Error('corrupt body'); } };
        } }; } },
        { async open() { return {
            async match() {}, async put() { throw new Error('quota exceeded'); }
        }; } }
    ]) {
        const client = createDictionaryCache({ baseURL, cacheStorage, fetch: async () => response('fresh') });
        const result = await client.load(url);
        assert.equal(result.source, 'network');
        assert.equal(text(result), 'fresh');
    }
    const client = createDictionaryCache({
        baseURL, get cacheStorage() { throw new Error('getter denied'); },
        fetch: async () => response('fresh')
    });
    assert.equal((await client.load(url)).source, 'network');
    assert.equal(await client.clear(), false);
});

test('unsuccessful cached responses and opaque network responses are not used or persisted', async () => {
    let writes = 0;
    const client = createDictionaryCache({
        baseURL,
        cacheStorage: { async open() { return {
            async match() { return new Response('error', { status: 500 }); },
            async put() { ++writes; }
        }; } },
        fetch: async () => ({ ok: true, status: 0, type: 'opaque' })
    });
    await assert.rejects(client.load(url), /failed/);
    assert.equal(writes, 0);
});

test('clear deletes only application cache', async () => {
    const { client, cacheStorage } = await setup(async () => response());
    await (await cacheStorage.open('other-app')).put(url, response());
    assert.equal(await client.clear(), true);
    assert.equal(cacheStorage.caches.has(DICTIONARY_CACHE_NAME), false);
    assert.equal(cacheStorage.caches.has('other-app'), true);
});

test('concurrent same-URL loads deduplicate with independently transferable bytes', async () => {
    let requests = 0;
    const { client } = await setup(async () => {
        ++requests;
        await new Promise(resolve => setTimeout(resolve, 10));
        return response('fresh');
    }, null);
    const [first, second] = await Promise.all([client.load(url), client.load('./word-readings.tsv#ignored')]);
    assert.equal(requests, 1);
    assert.notEqual(first.bytes.buffer, second.bytes.buffer);
    first.bytes.fill(0);
    assert.equal(text(second), 'fresh');
    await client.load(url);
    assert.equal(requests, 2);
});

test('clear while download is pending prevents persistent repopulation', async () => {
    let release;
    const fetching = new Promise(resolve => { release = resolve; });
    const { client, cacheStorage } = await setup(async () => { await fetching; return response('fresh'); }, null);
    const loading = client.load(url);
    await client.clear();
    release();
    assert.equal((await loading).source, 'network');
    assert.equal(cacheStorage.caches.get(DICTIONARY_CACHE_NAME)?.size ?? 0, 0);
});

test('cross-origin and non-HTTP inputs reject before fetch or cache access', async () => {
    const client = createDictionaryCache({
        baseURL, fetch: () => assert.fail('must not fetch'),
        cacheStorage: { open: () => assert.fail('must not open storage') }
    });
    for (const value of ['https://other.test/dict', 'data:text/plain,private', 'file:///private', 'ftp://example.test/a']) {
        await assert.rejects(client.load(value), /same-origin HTTP/);
    }
});

test('already aborted requests never touch storage or network', async () => {
    const controller = new AbortController();
    controller.abort();
    const client = createDictionaryCache({
        baseURL, fetch: () => assert.fail('must not fetch'),
        cacheStorage: { open: () => assert.fail('must not read cache') }
    });
    await assert.rejects(client.load(url, { signal: controller.signal }), { name: 'AbortError' });
});

test('abort reaches fetch and never returns cached bytes as offline success', async () => {
    const controller = new AbortController();
    let started;
    const fetching = new Promise(resolve => { started = resolve; });
    const { client, cacheStorage } = await setup(async (_, { signal }) => {
        assert.equal(signal, controller.signal);
        started();
        return new Promise((resolve, reject) => {
            signal.addEventListener('abort', () => reject(signal.reason), { once: true });
        });
    });
    const loading = client.load(url, { signal: controller.signal });
    await fetching;
    controller.abort();
    await assert.rejects(loading, { name: 'AbortError' });
    assert.equal(await (await (await cacheStorage.open(DICTIONARY_CACHE_NAME)).match(url)).text(), 'old');
});

test('AbortError from fetch or body rejects even when offline fallback exists', async () => {
    for (const fetch of [
        async () => { throw new DOMException('Cancelled', 'AbortError'); },
        async () => ({ ok: true, status: 200, async arrayBuffer() {
            throw new DOMException('Cancelled body', 'AbortError');
        } })
    ]) {
        const { client } = await setup(fetch);
        await assert.rejects(client.load(url), { name: 'AbortError' });
    }
});

test('cancelling one caller does not cancel an independent same-URL load', async () => {
    const controller = new AbortController();
    const { client } = await setup(async (_, { signal }) => {
        if (!signal) return response('fresh');
        return new Promise((resolve, reject) => {
            signal.addEventListener('abort', () => reject(signal.reason), { once: true });
            queueMicrotask(() => controller.abort());
        });
    });
    const results = await Promise.allSettled([
        client.load(url, { signal: controller.signal }), client.load(url)
    ]);
    assert.equal(results[0].status, 'rejected');
    assert.equal(results[0].reason.name, 'AbortError');
    assert.equal(results[1].status, 'fulfilled');
    assert.equal(text(results[1].value), 'fresh');
});
