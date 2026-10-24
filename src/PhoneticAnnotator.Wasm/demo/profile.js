import { loadBundledDictionary as loadDictionary, clearDictionaryCache } from './dictionary-cache.js';

const languages = [
    { language: 'ja-JP', reading: 'kJapanese', separator: '。' },
    { language: 'zh-CN', reading: 'kMandarin', separator: '。' },
    { language: 'ko', reading: 'kHangul', separator: ' ' },
    { language: 'th', reading: 'th', separator: ' ' }
];
const supplements = {
    'ja-JP': [['食べる', 'たべる'], ['山林', 'さんりん']],
    'zh-CN': [['你好', 'nǐ hǎo'], ['重庆', 'chóng qìng']],
    ko: [['한국', 'hanguk'], ['사람', 'saram'], ['학교', 'hakgyo'], ['한국어', 'hangugeo']],
    th: [['ภาษาไทย', 'phasa thai'], ['สวัสดี', 'sawatdi'], ['โรงเรียน', 'rong rian']]
};
const sizes = { tweet: 140, article: 2000, chapter: 12000 };
const activeWorkers = new Set();
const activeCacheOperations = new Set();
let running = false;

export function validateOptions(options = {}) {
    if (!options || typeof options !== 'object' || Array.isArray(options)) throw new TypeError('Expected an options object.');
    const allowed = new Set(['iterations', 'warmups', 'timeoutMs', 'buildLabel']);
    for (const key of Object.keys(options)) {
        if (!allowed.has(key)) throw new TypeError(`Unknown option: ${key}`);
    }
    const result = { iterations: 5, warmups: 2, timeoutMs: 60000, buildLabel: 'unspecified build / device', ...options };
    for (const [key, min, max] of [['iterations', 1, 50], ['warmups', 0, 10], ['timeoutMs', 1000, 300000]]) {
        if (!Number.isInteger(result[key]) || result[key] < min || result[key] > max) {
            throw new RangeError(`${key} must be an integer between ${min} and ${max}.`);
        }
    }
    if (typeof result.buildLabel !== 'string' || result.buildLabel.length > 100) {
        throw new TypeError('buildLabel must be a string of at most 100 characters.');
    }
    return result;
}

export function summarize(samples, characters) {
    const sorted = [...samples].sort((a, b) => a - b);
    const middle = Math.floor(sorted.length / 2);
    const medianMs = sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    return {
        samplesMs: samples, medianMs, p95Ms: sorted[Math.ceil(sorted.length * .95) - 1],
        charsPerSecond: medianMs > 0 ? characters * 1000 / medianMs : null
    };
}

function progress(message) {
    document.dispatchEvent(new CustomEvent('profile-progress', { detail: message }));
}

async function timed(action) {
    const start = performance.now();
    const value = await action();
    return { value, elapsedMs: performance.now() - start };
}

async function bounded(action, timeoutMs, description) {
    let timer;
    const controller = new AbortController();
    const operation = Promise.resolve().then(() => action(controller.signal));
    const entry = { controller };
    activeCacheOperations.add(entry);
    operation.then(
        () => activeCacheOperations.delete(entry),
        () => activeCacheOperations.delete(entry));
    let onAbort;
    try {
        return await Promise.race([
            operation,
            new Promise((_, reject) => {
                onAbort = () => reject(controller.signal.reason);
                controller.signal.addEventListener('abort', onAbort, { once: true });
                timer = setTimeout(() => {
                    const error = new Error(`${description} timed out.`);
                    controller.abort(error);
                }, timeoutMs);
            })
        ]);
    } finally {
        clearTimeout(timer);
        controller.signal.removeEventListener('abort', onAbort);
    }
}

// A fatal error settles every pending call and makes subsequent calls fail immediately.
export function createWorkerClient(timeoutMs) {
    const worker = new Worker(new URL('./phonetic-annotator-worker.js', import.meta.url), { type: 'module' });
    const pending = new Map();
    let nextId = 0;
    let failure;
    const client = {
        call(method, args = []) {
            if (failure) return Promise.reject(failure);
            return new Promise((resolve, reject) => {
                const id = ++nextId;
                const timer = setTimeout(() => client.close(new Error(`${method} worker round trip timed out.`)), timeoutMs);
                pending.set(id, { resolve, reject, timer });
                try { worker.postMessage({ id, method, args }); }
                catch (error) { client.close(error); }
            });
        },
        close(error = new Error('Profiling worker terminated.')) {
            if (failure) return;
            failure = error;
            for (const request of pending.values()) {
                clearTimeout(request.timer);
                request.reject(failure);
            }
            pending.clear();
            worker.terminate();
            activeWorkers.delete(client);
        }
    };
    worker.onmessage = ({ data }) => {
        if (data?.type === 'startup-error') {
            client.close(new Error(data.error?.message || 'Worker runtime startup failed.'));
            return;
        }
        const request = pending.get(data?.id);
        if (!request) return;
        clearTimeout(request.timer);
        pending.delete(data.id);
        if (data.ok) request.resolve(data.result);
        else request.reject(new Error(data.error?.message || 'Worker request failed.'));
    };
    worker.onerror = event => {
        event.preventDefault();
        client.close(new Error(event.message || 'Worker failed to load or execute.'));
    };
    worker.onmessageerror = () => client.close(new Error('Worker message could not be deserialized.'));
    activeWorkers.add(client);
    return client;
}

function fingerprint(bytes) {
    let hash = 2166136261;
    for (const byte of bytes) hash = Math.imul(hash ^ byte, 16777619);
    return `fnv1a32:${(hash >>> 0).toString(16).padStart(8, '0')}`;
}

function wordWorkloads(bytes) {
    const rows = new TextDecoder().decode(bytes).split(/\r?\n/u)
        .filter(line => line && !line.startsWith('#'))
        .map(line => line.split('\t'));
    const supplementalRows = [];
    const workloads = languages.map(item => {
        let words = rows.filter(row => row[0] === item.language && row[1] && row[2])
            .slice(0, 4).map(row => row[1]);
        const dictionary = words.length ? 'bundled' : 'synthetic-supplement';
        if (!words.length) {
            words = supplements[item.language].map(([word, reading]) => {
                supplementalRows.push(`${item.language}\t${word}\t${reading}`);
                return word;
            });
        }
        return { ...item, strategy: 'word', dictionary, seed: words.join(item.separator) + item.separator };
    });
    return { workloads, supplement: new TextEncoder().encode(supplementalRows.join('\n') + '\n') };
}

function unihanWorkloads(bytes) {
    const selected = new Map(languages.filter(item => item.reading !== 'th').map(item => [item.reading, []]));
    for (const line of new TextDecoder().decode(bytes).split(/\r?\n/u)) {
        const match = /^U\+([0-9A-F]+)\t(\w+)\t(.+)$/u.exec(line);
        const characters = match && selected.get(match[2]);
        if (characters && characters.length < 4) characters.push(String.fromCodePoint(parseInt(match[1], 16)));
    }
    return languages.filter(item => item.reading !== 'th').map(item => {
        const characters = selected.get(item.reading);
        if (!characters.length) throw new Error(`Bundled Unihan has no ${item.reading} readings; cannot report real throughput.`);
        return { ...item, strategy: 'unihan', dictionary: 'bundled', seed: characters.join('') + item.separator };
    });
}

function makeText(seed, length) {
    const characters = Array.from(seed);
    return Array.from({ length }, (_, i) => characters[i % characters.length]).join('');
}

function rubyCount(ast) {
    return (ast.type === 'ruby' ? 1 : 0) + (ast.children ?? []).reduce((sum, child) => sum + rubyCount(child), 0);
}

async function annotations(client, workload, options) {
    const results = [];
    for (const [size, characters] of Object.entries(sizes)) {
        const text = makeText(workload.seed, characters);
        const args = [text, JSON.stringify({
            language: workload.language, readingSystem: workload.reading, strategy: workload.strategy,
            pickFirstReading: true, alignOkurigana: false
        })];
        progress(`${workload.language} / ${workload.strategy} / ${size}`);
        // This correctness probe is excluded even when warmups=0; it prevents no-op throughput claims.
        const probe = JSON.parse(await client.call('AnnotateToJson', args));
        const annotatedNodes = rubyCount(probe);
        if (!annotatedNodes) throw new Error(`No annotations for ${workload.language}/${workload.strategy}; refusing misleading timings.`);
        for (let i = 0; i < options.warmups; i++) await client.call('AnnotateToJson', args);
        const samples = [];
        for (let i = 0; i < options.iterations; i++) {
            samples.push((await timed(() => client.call('AnnotateToJson', args))).elapsedMs);
        }
        results.push({
            language: workload.language, readingSystem: workload.reading, strategy: workload.strategy,
            dictionary: workload.dictionary, size, characters, utf16CodeUnits: text.length,
            annotatedNodes, ...summarize(samples, characters)
        });
    }
    return results;
}

async function pass(cacheState, options) {
    progress(`${cacheState}: fresh worker runtime initialization`);
    const start = performance.now();
    const client = createWorkerClient(options.timeoutMs);
    try {
        await client.call('initialize');
        const runtimeInitializeMs = performance.now() - start;
        const dictionaries = {};
        const data = {};
        for (const [name, path] of [['word', './word-readings.tsv'], ['unihan', './Unihan_Readings.txt']]) {
            progress(`${cacheState}: ${name} dictionary load`);
            const measured = await timed(() => bounded(
                signal => loadDictionary(new URL(path, import.meta.url).href, { signal }),
                options.timeoutMs, `${name} dictionary load`));
            const { bytes, source } = measured.value;
            if (!(bytes instanceof Uint8Array) || !bytes.length
                || !['network', 'cache', 'offline-cache'].includes(source)) {
                throw new Error('Unexpected dictionary-cache response.');
            }
            data[name] = bytes;
            dictionaries[name] = { loadMs: measured.elapsedMs, source, bytes: bytes.length, fingerprint: fingerprint(bytes) };
        }
        progress(`${cacheState}: bundled word dictionary indexing`);
        dictionaries.word.indexMs = (await timed(() => client.call('LoadDictionaryChunk', [data.word]))).elapsedMs;
        const { workloads, supplement } = wordWorkloads(data.word);
        const synthetic = { loaded: workloads.some(item => item.dictionary === 'synthetic-supplement') };
        if (synthetic.loaded) {
            synthetic.bytes = supplement.length;
            synthetic.fingerprint = fingerprint(supplement);
            synthetic.indexMs = (await timed(() => client.call('LoadDictionaryChunk', [supplement]))).elapsedMs;
        }
        const workloadsResults = [];
        for (const workload of workloads) workloadsResults.push(...await annotations(client, workload, options));
        const unihanIndexes = [];
        for (const workload of unihanWorkloads(data.unihan)) {
            progress(`${cacheState}: Unihan ${workload.reading} indexing`);
            const indexMs = (await timed(() => client.call('LoadUnihanDictionary', [data.unihan, workload.reading]))).elapsedMs;
            unihanIndexes.push({ readingSystem: workload.reading, indexMs });
            workloadsResults.push(...await annotations(client, workload, options));
        }
        return { cacheState, runtimeInitializeMs, dictionaries, syntheticSupplement: synthetic, unihanIndexes, workloads: workloadsResults };
    } finally {
        client.close();
    }
}

/**
 * Local-only API: await runProfile({ iterations: 5, warmups: 2, timeoutMs: 60000,
 * buildLabel: 'Release AOT / phone model' }). Rejects on any invalid or failed workload.
 */
export async function runProfile(options = {}) {
    const settings = validateOptions(options);
    if (running) throw new Error('A profile is already running.');
    if (activeCacheOperations.size) {
        throw new Error('A previous dictionary cache operation is still settling; wait before retrying.');
    }
    running = true;
    try {
        progress('Clearing dictionary application cache (not browser HTTP/runtime cache)');
        const clear = await timed(() => bounded(clearDictionaryCache, settings.timeoutMs, 'Dictionary cache clear'));
        if (clear.value === false) {
            throw new Error('Dictionary application cache could not be cleared. Use HTTPS (or localhost) with cache storage enabled; a cold/warm cache comparison is unavailable.');
        }
        const cold = await pass('cold-dictionary-cache', settings);
        const warm = await pass('warm-dictionary-cache', settings);
        const result = {
            schemaVersion: 1, workloadVersion: 'synthetic-multilingual-v1', createdAt: new Date().toISOString(),
            options: settings,
            environment: {
                userAgent: navigator.userAgent, hardwareConcurrency: navigator.hardwareConcurrency ?? null,
                deviceMemoryGiB: navigator.deviceMemory ?? null, secureContext: isSecureContext,
                crossOriginIsolated, visibilityState: document.visibilityState
            },
            units: { duration: 'ms', characters: 'Unicode code points', throughput: 'characters/second at median round-trip latency' },
            methodology: {
                runtime: 'Fresh module worker per pass; browser/runtime HTTP caches are not cleared.',
                dictionary: 'Cold clears only dictionary application cache; reported source is authoritative. Warm is a subsequent load, not a guaranteed cache hit.',
                indexing: 'Single worker round-trip observation per dictionary/reading, including byte cloning and managed marshaling; excludes download.',
                annotation: 'Sequential AnnotateToJson worker round trip includes structured cloning, interop and managed JSON serialization; excludes main-thread JSON parsing.',
                exclusions: 'One correctness probe and configured warmups per workload are excluded; indexing/download are single observations, not latency distributions.',
                percentile: 'p95 nearest rank; median averages two middle samples for even counts.',
                timeouts: 'Worker timeouts terminate the worker and reject pending calls. Dictionary timeouts/pagehide abort fetch/body reads. CacheStorage operations already in progress cannot be cancelled; retries are blocked until prior operations settle. Each new run clears application cache first; do not interpret a retry as a cold browser/network run.',
                content: 'Deterministic repeated synthetic inputs, actual bundled dictionaries plus explicitly identified in-memory word TSV supplements.',
                caveats: 'No managed-only compute or physical-device claim. Browser scheduling, JIT, timer precision, thermal state and background tabs affect results. FNV-1a fingerprints are non-cryptographic.'
            },
            dictionaryCacheClearSucceeded: clear.value === true,
            dictionaryCacheClearMs: clear.elapsedMs, passes: [cold, warm]
        };
        progress('Profile complete. Results stayed in this browser.');
        return result;
    } finally {
        running = false;
    }
}

window.addEventListener('pagehide', () => {
    for (const entry of activeCacheOperations) entry.controller.abort(new Error('Profiling page closed.'));
    for (const client of activeWorkers) client.close(new Error('Profiling page closed.'));
});

const form = document.querySelector('#profile-form');
if (form) {
    const status = document.querySelector('#profile-status');
    const errorOutput = document.querySelector('#profile-error');
    const download = document.querySelector('#download');
    const summary = document.querySelector('#profile-summary');
    let result;
    document.addEventListener('profile-progress', event => { status.textContent = event.detail; });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const controls = document.querySelector('#profile-controls');
        controls.disabled = true;
        download.disabled = true;
        errorOutput.textContent = '';
        summary.textContent = '';
        result = undefined;
        try {
            result = await runProfile({
                iterations: Number(document.querySelector('#iterations').value),
                warmups: Number(document.querySelector('#warmups').value),
                timeoutMs: Number(document.querySelector('#timeout-ms').value),
                buildLabel: document.querySelector('#build-label').value
            });
            summary.textContent = JSON.stringify(result, null, 2);
            download.disabled = false;
        } catch (error) {
            status.textContent = 'Profile failed; no complete result is available.';
            errorOutput.textContent = error.message;
        } finally {
            controls.disabled = false;
        }
    });
    download.addEventListener('click', () => {
        if (!result) return;
        const url = URL.createObjectURL(new Blob([JSON.stringify(result, null, 2)], { type: 'application/json' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = 'phonetic-wasm-profile.json';
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
}
