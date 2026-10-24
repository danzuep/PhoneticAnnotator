import { test, expect } from '@playwright/test';

test('.NET module worker: ready, bytes, ordered updates, correlated errors and recovery', async ({ page }) => {
    await page.goto('./');
    const result = await page.evaluate(async () => {
        const worker = new Worker(new URL('./phonetic-annotator-worker.js', location.href), { type: 'module' });
        try {
            const pending = new Map();
            const events = [];
            worker.onmessage = ({ data }) => {
                if (data.type) events.push(data);
                else { pending.get(data.id)?.(data); pending.delete(data.id); }
            };
            let id = 0;
            const call = (method, args) => new Promise(resolve => {
                const requestId = ++id;
                pending.set(requestId, resolve);
                worker.postMessage({ id: requestId, method, args });
            });
            const encode = text => new TextEncoder().encode(text);
            const responses = await Promise.all([
                call('initialize', []),
                call('LoadDictionaryChunk', [encode('zh-CN\t你好\tnǐ hǎo\n')]),
                call('LoadDictionaryChunk', [encode('invalid\n')]),
                call('AnnotateToJson', ['你好', JSON.stringify({ language: 'zh-CN', strategy: 'word' })]),
                call('constructor', []),
                call('LoadUnihanDictionary', [encode('U+4F60\tkMandarin\tnǐ\n'), 'kMandarin']),
                call('AnnotateToHtml', ['你', JSON.stringify({ language: 'zh-CN', strategy: 'unihan' })])
            ]);
            return { events, responses };
        } finally {
            worker.terminate();
        }
    });
    expect(result.events).toEqual([{ type: 'ready' }]);
    expect(result.responses.map(response => response.id)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    expect(result.responses.map(response => response.ok)).toEqual([true, true, false, true, false, true, true]);
    expect(JSON.parse(result.responses[3].result).children[0].glossText).toBe('nǐ hǎo');
    expect(result.responses[4].error.message).toContain('Unknown worker method');
    expect(result.responses[6].result).toContain('nǐ');
});

test('.NET module worker: configurable runtime URL and sticky startup errors', async ({ page }) => {
    await page.goto('./');
    const messages = await page.evaluate(async () => {
        const worker = new Worker(new URL('./phonetic-annotator-worker.js', location.href), { type: 'module' });
        try {
            return await new Promise((resolve, reject) => {
                const received = [];
                worker.onerror = event => reject(new Error(event.message));
                worker.onmessage = ({ data }) => {
                    received.push(data);
                    if (data.id === 'second') resolve(received);
                };
                worker.postMessage({
                    id: 'first', method: 'initialize',
                    args: [{ runtimeUrl: './missing-runtime-for-worker-test.js' }]
                });
                worker.postMessage({ id: 'second', method: 'AnnotateToJson', args: ['你好', '{}'] });
            });
        } finally {
            worker.terminate();
        }
    });
    expect(messages[0].type).toBe('startup-error');
    expect(messages[1]).toMatchObject({ id: 'first', ok: false });
    expect(messages[2]).toMatchObject({ id: 'second', ok: false, error: messages[1].error });
});
