import { dotnet } from './_framework/dotnet.js';

const status = document.querySelector('#status');
const controls = document.querySelector('#controls');
const text = document.querySelector('#text');
const language = document.querySelector('#language');
const format = document.querySelector('#format');
const alignment = document.querySelector('#alignment');
const dictionary = document.querySelector('#dictionary');
const preview = document.querySelector('#preview');
const latency = document.querySelector('#latency');
let requestId = 0;

try {
    const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet
        .withDiagnosticTracing(false)
        .create();

    setModuleImports('phonetic-demo', {
        onReady: () => { status.textContent = '.NET initialized; loading example dictionary…'; }
    });

    const exports = await getAssemblyExports('PhoneticAnnotator.Wasm');
    const interop = exports.PhoneticAnnotator.Wasm.Interop;
    await runMain(getConfig().mainAssemblyName, []);

    const response = await fetch('./word-readings.tsv');
    if (!response.ok) {
        throw new Error(`Example dictionary request failed (${response.status}).`);
    }
    await interop.LoadDictionaryChunk(new Uint8Array(await response.arrayBuffer()));

    async function render() {
        const id = ++requestId;
        const outputFormat = format.value;
        const options = JSON.stringify({
            language: language.value,
            alignOkurigana: alignment.checked
        });
        const start = performance.now();
        try {
            const result = await (outputFormat === 'html'
                ? interop.AnnotateToHtml(text.value, options)
                : interop.AnnotateToJson(text.value, options));
            const elapsed = performance.now() - start;
            if (id !== requestId) return;

            if (outputFormat === 'html') {
                // HtmlRubyVisitor encodes all source text and dictionary readings.
                preview.innerHTML = result;
            } else {
                const pre = document.createElement('pre');
                pre.textContent = JSON.stringify(JSON.parse(result), null, 2);
                preview.replaceChildren(pre);
            }
            latency.textContent = `${elapsed.toFixed(3)} ms`;
            status.textContent = 'Ready. Text and dictionaries stay in this browser.';
        } catch (error) {
            if (id !== requestId) return;
            preview.replaceChildren();
            latency.textContent = '— ms';
            status.textContent = `Annotation failed: ${error.message}`;
        }
    }

    text.addEventListener('input', render);
    for (const control of [language, format, alignment]) {
        control.addEventListener('change', render);
    }
    dictionary.addEventListener('change', async () => {
        ++requestId;
        controls.disabled = true;
        status.textContent = 'Loading dictionary chunks…';
        try {
            for (const file of dictionary.files) {
                await interop.LoadDictionaryChunk(new Uint8Array(await file.arrayBuffer()));
            }
            await render();
        } catch (error) {
            status.textContent = `Dictionary load failed (previous chunks retained): ${error.message}`;
        } finally {
            controls.disabled = false;
            dictionary.value = '';
        }
    });

    controls.disabled = false;
    await render();
} catch (error) {
    controls.disabled = true;
    status.textContent = `Startup failed: ${error.message}`;
}
