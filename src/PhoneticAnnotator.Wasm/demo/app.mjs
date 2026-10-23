import { startDemo } from './main.js';

await startDemo(async () => {
    const { dotnet } = await import('./_framework/dotnet.js');
    const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet
        .withDiagnosticTracing(false)
        .create();

    setModuleImports('phonetic-demo', { onReady: () => {} });
    const exports = await getAssemblyExports('PhoneticAnnotator.Wasm');
    await runMain(getConfig().mainAssemblyName, []);
    return exports.PhoneticAnnotator.Wasm.Interop;
}, '.NET 10 WASM');
