export async function startDemo(createInterop, runtimeName) {

const status = document.querySelector('#status');
const controls = document.querySelector('#controls');
const text = document.querySelector('#text');
const language = document.querySelector('#language');
const format = document.querySelector('#format');
const alignment = document.querySelector('#alignment');
const pickFirst = document.querySelector('#pick-first');
const strategy = document.querySelector('#strategy');
const dictionary = document.querySelector('#dictionary');
const preview = document.querySelector('#preview');
const latency = document.querySelector('#latency');
let requestId = 0;

try {
    document.querySelector('#runtime').textContent = runtimeName;
    status.textContent = `Starting ${runtimeName}…`;
    const links = document.querySelectorAll('nav a');
    const isNetWasm = runtimeName === 'NetWasm';
    links[0].href = isNetWasm ? '../' : './';
    links[1].href = isNetWasm ? './' : './netwasm/';
    const interop = await createInterop();

    const response = await fetch('./word-readings.tsv');
    if (!response.ok) {
        throw new Error(`Example dictionary request failed (${response.status}).`);
    }
    await interop.LoadDictionaryChunk(new Uint8Array(await response.arrayBuffer()));
    let unihanBytes;
    let loadedReading;

    async function loadReading() {
        ++requestId;
        controls.disabled = true;
        try {
            if (language.value === 'ar') strategy.value = 'word';
            const reading = language.selectedOptions[0].dataset.reading;
            if (strategy.value === 'unihan' && loadedReading !== reading) {
                status.textContent = 'Loading Unihan character readings…';
                if (!unihanBytes) {
                    const data = await fetch('./Unihan_Readings.txt');
                    if (!data.ok) throw new Error(`Unihan request failed (${data.status}).`);
                    unihanBytes = new Uint8Array(await data.arrayBuffer());
                }
                await interop.LoadUnihanDictionary(unihanBytes, reading);
                loadedReading = reading;
            }
            await render();
        } catch (error) {
            preview.replaceChildren();
            latency.textContent = '— ms';
            status.textContent = `Dictionary load failed: ${error.message}`;
        } finally {
            controls.disabled = false;
        }
    }

    async function render() {
        const id = ++requestId;
        const outputFormat = format.value;
        const options = JSON.stringify({
            language: language.value,
            readingSystem: language.selectedOptions[0].dataset.reading,
            strategy: strategy.value,
            pickFirstReading: pickFirst.checked,
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
    for (const control of [format, alignment, pickFirst]) {
        control.addEventListener('change', render);
    }
    for (const control of [language, strategy]) {
        control.addEventListener('change', loadReading);
    }
    document.querySelector('#example').addEventListener('click', () => {
        text.value = language.value === 'ar' ? 'كتب'
            : language.value === 'za' ? '𬺣'
            : language.value === 'ja-JP' && strategy.value === 'word' ? '食べる'
            : '的';
        render();
    });
    dictionary.addEventListener('change', async () => {
        ++requestId;
        controls.disabled = true;
        status.textContent = 'Loading dictionary chunks…';
        try {
            for (const file of dictionary.files) {
                await interop.LoadDictionaryChunk(new Uint8Array(await file.arrayBuffer()));
            }
            strategy.value = 'word';
            await render();
        } catch (error) {
            status.textContent = `Dictionary load failed (previous chunks retained): ${error.message}`;
        } finally {
            controls.disabled = false;
            dictionary.value = '';
        }
    });

    await loadReading();
} catch (error) {
    controls.disabled = true;
    status.textContent = `Startup failed: ${error.message}`;
}
}
