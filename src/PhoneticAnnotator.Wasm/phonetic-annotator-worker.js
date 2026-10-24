// Module worker. Send { id, method, args }; receive { id, ok, result/error }.
const methods = new Map([
    ['AnnotateToHtml', ['string', 'string']],
    ['AnnotateToJson', ['string', 'string']],
    ['LoadUnihanDictionary', ['bytes', 'string']],
    ['LoadDictionaryChunk', ['bytes']]
]);
let startup;
let runtimeUrl;
let queue = Promise.resolve();

function serializeError(error) {
    return { name: error?.name || 'Error', message: error?.message || String(error) };
}

function initialize(options = {}) {
    if (!options || typeof options !== 'object' || Array.isArray(options)
        || (options.runtimeUrl !== undefined && (typeof options.runtimeUrl !== 'string' || !options.runtimeUrl))) {
        throw new TypeError('initialize expects an optional { runtimeUrl: string }.');
    }
    const requestedUrl = new URL(options.runtimeUrl ?? './_framework/dotnet.js', import.meta.url).href;
    if (startup) {
        if (options.runtimeUrl !== undefined && requestedUrl !== runtimeUrl) {
            throw new Error('The runtime URL cannot change after initialization.');
        }
        return startup;
    }
    runtimeUrl = requestedUrl;
    startup = (async () => {
        const { dotnet } = await import(runtimeUrl);
        let ready = false;
        const imports = { onReady: () => { ready = true; } };
        const runtime = await dotnet
            .withDiagnosticTracing(false)
            .withModuleConfig({ imports: { 'phonetic-demo': imports } })
            .create();
        runtime.setModuleImports('phonetic-demo', imports);
        const exports = await runtime.getAssemblyExports('PhoneticAnnotator.Wasm');
        // runMain keeps the runtime alive; run()/runMainAndExit() would shut down JS exports.
        const exitCode = await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
        if (exitCode !== 0) throw new Error(`Runtime startup exited with code ${exitCode}.`);
        if (!ready) throw new Error('Runtime Main completed without the onReady callback.');
        const interop = exports.PhoneticAnnotator.Wasm.Interop;
        for (const method of methods.keys()) {
            if (typeof interop[method] !== 'function') throw new Error(`Missing .NET export: ${method}`);
        }
        self.postMessage({ type: 'ready' });
        return interop;
    })().catch(error => {
        self.postMessage({ type: 'startup-error', error: serializeError(error) });
        throw error;
    });
    return startup;
}

async function dispatch(request) {
    const { method, args } = request;
    if (typeof method !== 'string' || !Array.isArray(args)) throw new TypeError('Expected { id, method, args: [] }.');
    if (method === 'initialize') {
        if (args.length > 1) throw new TypeError('initialize accepts at most one argument.');
        await initialize(...args);
        return;
    }
    if (!methods.has(method)) throw new Error(`Unknown worker method: ${method}`);
    const parameters = methods.get(method);
    if (args.length !== parameters.length || args.some((arg, index) =>
        parameters[index] === 'bytes' ? !(arg instanceof Uint8Array) : typeof arg !== 'string')) {
        throw new TypeError(`Invalid arguments for ${method}; expected ${parameters.join(', ')}.`);
    }
    const interop = await initialize();
    return await interop[method](...args);
}

self.addEventListener('message', event => {
    const request = event.data;
    const id = request?.id;
    const validId = typeof id === 'string' || (typeof id === 'number' && Number.isFinite(id));
    // Every failure is caught inside this operation so later messages can still execute.
    queue = queue.then(async () => {
        try {
            if (!validId) throw new TypeError('Request id must be a string or finite number.');
            const result = await dispatch(request);
            self.postMessage({ id, ok: true, result });
        } catch (error) {
            self.postMessage({ id: validId ? id : null, ok: false, error: serializeError(error) });
        }
    });
});
