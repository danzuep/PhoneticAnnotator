export const DICTIONARY_CACHE_NAME = 'phonetic-annotator-bundled-dictionaries-v1';

/**
 * Same-origin bundled dictionaries only; never pass uploads or source text here.
 * load(url) returns { bytes: Uint8Array, source: 'network' | 'cache' | 'offline-cache' }.
 * Every load revalidates online; 'cache' means a conditional request returned 304.
 * clear() deletes only this application's cache and returns false if storage is unavailable.
 * The factory also allows isolated fetch/CacheStorage/baseURL adapters for testing.
 */
export function createDictionaryCache(options = {}) {
    const pending = new Map();
    let generation = 0;

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

    function storage() {
        return 'cacheStorage' in options ? options.cacheStorage : globalThis.caches;
    }

    async function download(url, startedGeneration) {
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
            // Private browsing, eviction and corrupt entries must not prevent downloads.
            cached = undefined;
        }
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
                headers, cache: 'no-store', mode: 'same-origin', credentials: 'same-origin'
            });
        } catch (error) {
            if (cached) return { bytes: cachedBytes, source: 'offline-cache' };
            throw error;
        }
        if (response.status === 304 && cached && headers.size !== 0) {
            return { bytes: cachedBytes, source: 'cache' };
        }
        if (!response.ok || response.type === 'opaque') {
            throw new Error(`Dictionary request failed (${response.status}): ${url}`);
        }
        let bytes;
        try {
            bytes = new Uint8Array(await response.arrayBuffer());
        } catch (error) {
            if (cached) return { bytes: cachedBytes, source: 'offline-cache' };
            throw error;
        }
        try {
            if (cache && generation === startedGeneration) {
                await cache.put(url, new Response(bytes, {
                    status: response.status, statusText: response.statusText, headers: response.headers
                }));
            }
        } catch {
            // Quota and write failures must never turn a successful download into an error.
        }
        return { bytes, source: 'network' };
    }

    return {
        async load(value) {
            const url = resolveUrl(value);
            if (!pending.has(url)) {
                const task = download(url, generation).finally(() => {
                    if (pending.get(url) === task) pending.delete(url);
                });
                pending.set(url, task);
            }
            const result = await pending.get(url);
            // Callers may transfer their bytes to a worker without detaching another caller's data.
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
export const loadBundledDictionary = url => bundledDictionaries.load(url);
export const clearDictionaryCache = () => bundledDictionaries.clear();
