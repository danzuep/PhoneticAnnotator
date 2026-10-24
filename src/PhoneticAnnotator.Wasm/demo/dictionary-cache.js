export const DICTIONARY_CACHE_NAME = 'phonetic-annotator-bundled-dictionaries-v1';

/**
 * Bundled same-origin HTTP(S) dictionaries only, never uploads or source text.
 * load(url, {signal}?) returns {bytes: Uint8Array, source: 'network'|'cache'|'offline-cache'}.
 * 'cache' means an online conditional request returned 304; every load revalidates.
 * clear() returns false if storage is unavailable, and deletes only our named cache.
 * Adapters are optional and make this API usable in isolated tests and workers.
 * Cancellable loads are isolated; only unsignaled same-URL calls are deduplicated.
 * Aborts reject and never become offline-cache success.
 */
export function createDictionaryCache(options = {}) {
    const pending = new Map();
    let generation = 0;
    function storage() {
        return 'cacheStorage' in options ? options.cacheStorage : globalThis.caches;
    }
    function resolveUrl(value) {
        const base = new URL(options.baseURL ?? globalThis.location?.href ?? import.meta.url);
        const url = new URL(value, base);
        if (!['http:', 'https:'].includes(url.protocol) || url.origin !== base.origin
            || url.username || url.password) {
            throw new Error('Bundled dictionaries require a same-origin HTTP(S) URL.');
        }
        url.hash = '';
        return url.href;
    }
    async function download(url, startedGeneration, signal) {
        signal?.throwIfAborted();
        let cache;
        let cached;
        let cachedBytes;
        try {
            cache = await storage()?.open(DICTIONARY_CACHE_NAME);
            cached = await cache?.match(url);
            if (cached?.ok && cached.type !== 'opaque') {
                cachedBytes = new Uint8Array(await cached.arrayBuffer());
                if (!cachedBytes.length) cached = undefined;
            } else {
                cached = undefined;
            }
        } catch {
            // Storage denial, eviction and corrupt entries must not prevent downloads.
            cached = undefined;
        }
        signal?.throwIfAborted();
        const headers = new Headers();
        if (cached) {
            const etag = cached.headers.get('ETag');
            const modified = cached.headers.get('Last-Modified');
            if (etag) headers.set('If-None-Match', etag);
            if (modified) headers.set('If-Modified-Since', modified);
        }
        let response;
        try {
            response = await (options.fetch ?? globalThis.fetch)(url, {
                headers, cache: 'no-store', mode: 'same-origin', credentials: 'same-origin', signal
            });
        } catch (error) {
            signal?.throwIfAborted();
            if (error?.name === 'AbortError') throw error;
            if (cached) return { bytes: cachedBytes, source: 'offline-cache' };
            throw error;
        }
        signal?.throwIfAborted();
        if (response.status === 304 && cached
            && (headers.has('If-None-Match') || headers.has('If-Modified-Since'))) {
            return { bytes: cachedBytes, source: 'cache' };
        }
        if (!response.ok || response.type === 'opaque') {
            throw new Error(`Dictionary request failed (${response.status}): ${url}`);
        }
        let bytes;
        try {
            bytes = new Uint8Array(await response.arrayBuffer());
        } catch (error) {
            signal?.throwIfAborted();
            if (error?.name === 'AbortError') throw error;
            if (cached) return { bytes: cachedBytes, source: 'offline-cache' };
            throw error;
        }
        signal?.throwIfAborted();
        try {
            if (cache && generation === startedGeneration) {
                await cache.put(url, new Response(bytes, {
                    status: response.status, statusText: response.statusText, headers: response.headers
                }));
            }
        } catch {
            // Quota failures never turn a successful download into an error.
        }
        signal?.throwIfAborted();
        return { bytes, source: 'network' };
    }
    return {
        async load(value, { signal } = {}) {
            const url = resolveUrl(value);
            if (signal) {
                const result = await download(url, generation, signal);
                return { bytes: result.bytes.slice(), source: result.source };
            }
            if (!pending.has(url)) {
                const task = download(url, generation).finally(() => {
                    if (pending.get(url) === task) pending.delete(url);
                });
                pending.set(url, task);
            }
            const result = await pending.get(url);
            // Worker transfers must not detach another caller's buffer.
            return { bytes: result.bytes.slice(), source: result.source };
        },
        async clear() {
            ++generation;
            pending.clear();
            try {
                const cacheStorage = storage();
                if (!cacheStorage) return false;
                await cacheStorage.delete(DICTIONARY_CACHE_NAME);
                return true;
            } catch {
                return false;
            }
        }
    };
}

const bundledDictionaries = createDictionaryCache();
export const loadBundledDictionary = (url, options) => bundledDictionaries.load(url, options);
export const loadDictionary = loadBundledDictionary;
export const clearDictionaryCache = () => bundledDictionaries.clear();
