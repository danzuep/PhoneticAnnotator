import { startDemo } from './main.js';

await startDemo(async () => {
    const { open } = await import('./browser/PhoneticAnnotator.NetWasm.mjs');
    const results = new Map();
    let requestId = 0;
    const app = await open({
        modules: { 'phonetic.results': {
            complete: (id, result) => results.set(id, result)
        } }
    });
    // NetWasm 0.10.2 cannot return Task<string> across JSExport.
    async function annotate(method, text, options) {
        const id = ++requestId;
        try {
            await app[method](id, text, options);
            if (!results.has(id)) throw new Error('Annotation completed without a result.');
            return results.get(id);
        } finally {
            results.delete(id);
        }
    }
    return {
        AnnotateToHtml: (text, options) => annotate('AnnotateToHtml', text, options),
        AnnotateToJson: (text, options) => annotate('AnnotateToJson', text, options),
        LoadDictionaryChunk: data => app.LoadDictionaryChunk(data)
    };
}, 'NetWasm');
