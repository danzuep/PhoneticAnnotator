import test from 'node:test';
import assert from 'node:assert/strict';
import { Worker } from 'node:worker_threads';

const workerUrl = new URL('../../src/PhoneticAnnotator.Wasm/phonetic-annotator-worker.js', import.meta.url);
const runtime = `
let starts = 0, imports, config, updates = [];
const pause = () => new Promise(resolve => setTimeout(resolve, 15));
const api = {
    async LoadDictionaryChunk(bytes) {
        await pause();
        if (bytes[0] === 0) throw new Error('Invalid dictionary');
        updates.push(bytes[0]);
    },
    async LoadUnihanDictionary(bytes, readingId) {
        await pause();
        updates.push(readingId + ':' + bytes[0]);
    },
    async AnnotateToJson(text, optionsJson) {
        JSON.parse(optionsJson);
        return JSON.stringify({ text, updates: [...updates], starts });
    },
    async AnnotateToHtml(text, optionsJson) { return '<ruby>' + text + '</ruby>'; }
};
export const dotnet = {
    withDiagnosticTracing(value) { if (value !== false) throw Error('tracing'); return this; },
    withModuleConfig(value) {
        if (typeof value.imports['phonetic-demo'].onReady !== 'function') throw Error('Missing imports');
        config = value; return this;
    },
    async create() {
        starts++;
        return {
            setModuleImports(name, value) {
                if (name !== 'phonetic-demo' || value !== config.imports[name]) throw Error('imports');
                imports = value;
            },
            async getAssemblyExports(name) {
                if (name !== 'PhoneticAnnotator.Wasm') throw Error('assembly');
                return { PhoneticAnnotator: { Wasm: { Interop: api } } };
            },
            getConfig() { return { mainAssemblyName: 'Main.dll' }; },
            async runMain(name, args) {
                if (name !== 'Main.dll' || args.length) throw Error('main');
                imports.onReady(); return 0;
            }
        };
    }
};`;
const runtimeUrl = `data:text/javascript,${encodeURIComponent(runtime)}`;

async function harness(t) {
    const worker = new Worker(`
        const { parentPort } = require('node:worker_threads');
        globalThis.self = {
            postMessage: message => parentPort.postMessage(message),
            addEventListener: (name, listener) => parentPort.on(name, data => listener({ data }))
        };
        import(${JSON.stringify(workerUrl.href)}).then(() => parentPort.postMessage({ bridgeReady: true }));
    `, { eval: true });
    t.after(() => worker.terminate());
    const pending = new Map();
    const events = [];
    await new Promise((resolve, reject) => {
        worker.once('error', reject);
        worker.on('message', message => {
            if (message.bridgeReady) resolve();
            else if (message.type) events.push(message);
            else {
                pending.get(message.id)?.(message);
                pending.delete(message.id);
            }
        });
    });
    let id = 0;
    return {
        events,
        send(method, args, requestId = ++id) {
            return new Promise(resolve => {
                const responseId = typeof requestId === 'string'
                    || (typeof requestId === 'number' && Number.isFinite(requestId)) ? requestId : null;
                pending.set(responseId, resolve);
                worker.postMessage({ id: requestId, method, args });
            });
        }
    };
}

test('module worker initializes once, correlates calls and serializes dictionary updates', { timeout: 10_000 }, async t => {
    const client = await harness(t);
    const calls = [
        client.send('initialize', [{ runtimeUrl }], 'startup'),
        client.send('LoadDictionaryChunk', [new Uint8Array([1])]),
        client.send('LoadDictionaryChunk', [new Uint8Array([0])]),
        client.send('LoadUnihanDictionary', [new Uint8Array([2]), 'kMandarin']),
        client.send('AnnotateToJson', ['你好', '{}'], 'annotation')
    ];
    const responses = await Promise.all(calls);
    assert.equal(responses[0].id, 'startup');
    assert.equal(responses[0].result, undefined);
    assert.equal(responses[1].ok, true);
    assert.equal(responses[2].ok, false);
    assert.match(responses[2].error.message, /Invalid dictionary/);
    assert.equal(responses[3].ok, true);
    assert.equal(responses[4].id, 'annotation');
    assert.deepEqual(JSON.parse(responses[4].result), { text: '你好', updates: [1, 'kMandarin:2'], starts: 1 });
    assert.deepEqual(client.events, [{ type: 'ready' }]);
    assert.equal((await client.send('initialize', [])).ok, true);
    assert.equal((await client.send('initialize', [{ runtimeUrl: './different.js' }])).ok, false);
    assert.equal(JSON.parse((await client.send('AnnotateToJson', ['', '{}'])).result).starts, 1);
});

test('worker rejects invalid requests and inherited methods without poisoning the queue', { timeout: 10_000 }, async t => {
    const client = await harness(t);
    for (const method of ['constructor', 'toString', '__proto__', 'exit', 'unknown']) {
        assert.match((await client.send(method, [])).error.message, /Unknown worker method/);
    }
    assert.equal(client.events.length, 0);
    assert.equal((await client.send('initialize', [{ runtimeUrl: 42 }])).ok, false);
    assert.equal((await client.send('initialize', [null])).ok, false);
    assert.equal((await client.send('initialize', [{}, {}])).ok, false);
    assert.equal((await client.send('LoadDictionaryChunk', [[1, 2]])).ok, false);
    assert.equal((await client.send('AnnotateToJson', ['text'])).ok, false);
    assert.equal((await client.send('AnnotateToJson', 'not an array')).ok, false);
    assert.equal((await client.send('AnnotateToJson', ['text', '{}'], null)).id, null);
    assert.equal((await client.send('AnnotateToJson', ['text', '{}'], Infinity)).id, null);
    assert.equal((await client.send('initialize', [{ runtimeUrl }])).ok, true);
    assert.equal((await client.send('AnnotateToJson', ['text', 'invalid json'])).ok, false);
    assert.equal((await client.send('AnnotateToHtml', ['text', '{}'])).result, '<ruby>text</ruby>');
});

test('startup errors are reported once and reject every correlated request', { timeout: 10_000 }, async t => {
    const client = await harness(t);
    const brokenUrl = `data:text/javascript,${encodeURIComponent('throw new Error("Startup failed")')}`;
    const first = await client.send('initialize', [{ runtimeUrl: brokenUrl }]);
    const second = await client.send('AnnotateToJson', ['text', '{}']);
    assert.equal(first.ok, false);
    assert.equal(second.ok, false);
    assert.match(first.error.message, /Startup failed/);
    assert.deepEqual(second.error, first.error);
    assert.deepEqual(client.events, [{ type: 'startup-error', error: first.error }]);
});

test('missing readiness and nonzero Main exits fail startup', { timeout: 10_000 }, async t => {
    for (const replacement of ['return 7;', 'return 0;']) {
        const client = await harness(t);
        const noReady = runtime.replace('imports.onReady(); return 0;', replacement);
        const response = await client.send('initialize', [{
            runtimeUrl: `data:text/javascript,${encodeURIComponent(noReady)}`
        }]);
        assert.equal(response.ok, false);
        assert.match(response.error.message, /code 7|without the onReady/);
    }
});
