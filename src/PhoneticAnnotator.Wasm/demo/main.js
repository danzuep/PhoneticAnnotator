const demoModuleStartedAt = performance.now();

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
const metadata = document.querySelector('#metadata');
const characterMetadata = document.querySelector('#character-metadata');
const diagnostics = document.querySelector('#diagnostics');
const timings = new Map();
const characterFields = new Map();
let unihanBytes;
let unihanText;
let requestId = 0;

function selectedLanguage() {
    return language.selectedOptions[0].dataset.language ?? language.value;
}

function showTimings() {
    const list = document.querySelector('#load-timings');
    list.replaceChildren();
    const navigation = performance.getEntriesByType('navigation')[0];
    const values = new Map([
        ['Page to demo module execution', demoModuleStartedAt],
        ['Document response', navigation?.responseEnd ?? 0],
        ['DOM ready', navigation?.domContentLoadedEventEnd ?? 0],
        ['Page load event', navigation?.loadEventEnd ?? 0],
        ...timings
    ]);
    for (const [name, elapsed] of values) {
        const term = document.createElement('dt');
        term.textContent = name;
        const value = document.createElement('dd');
        value.textContent = elapsed > 0 ? `${elapsed.toFixed(1)} ms` : 'Pending / unavailable';
        list.append(term, value);
    }
    const resources = document.querySelector('#resource-timings');
    resources.replaceChildren();
    if (!diagnostics.open) return;
    for (const entry of performance.getEntriesByType('resource')) {
        const row = document.createElement('p');
        row.textContent = `${new URL(entry.name).pathname}: ${entry.duration.toFixed(1)} ms; `
            + `${entry.transferSize} transferred, ${entry.encodedBodySize} encoded, ${entry.decodedBodySize} decoded bytes`;
        resources.append(row);
    }
}

async function measure(name, action) {
    const start = performance.now();
    try {
        return await action();
    } finally {
        timings.set(name, performance.now() - start);
        showTimings();
    }
}

async function download(path, name) {
    return measure(name, async () => {
        const response = await fetch(path);
        if (!response.ok) throw new Error(`${name} request failed (${response.status}).`);
        return new Uint8Array(await response.arrayBuffer());
    });
}

function renderMetadata() {
    if (!metadata.open || !unihanBytes) return;
    const characters = [...new Set([...text.value].filter(character => !/\s/u.test(character)))];
    const missing = new Set(characters.filter(character => !characterFields.has(character)));
    if (missing.size) {
        unihanText ??= new TextDecoder().decode(unihanBytes);
        for (const character of missing) characterFields.set(character, []);
        for (const match of unihanText.matchAll(/^U\+([0-9A-F]+)\t(\w+)\t(.+)$/gm)) {
            const character = String.fromCodePoint(parseInt(match[1], 16));
            if (missing.has(character)) characterFields.get(character).push([match[2], match[3]]);
        }
    }
    const fields = {
        'ja-JP': ['kJapanese', 'kJapaneseOn', 'kJapaneseKun'],
        'zh-CN': ['kMandarin', 'kHanyuPinlu', 'kHanyuPinyin', 'kTGHZ2013', 'kXHC1983', 'kSMSZD2003Readings', 'kTang', 'kFanqie'],
        'zh-HK': ['kCantonese', 'kSMSZD2003Readings', 'kTang', 'kFanqie'],
        ko: ['kHangul', 'kKorean'], vi: ['kVietnamese'], za: ['kZhuang'], ar: []
    }[selectedLanguage()] ?? [];
    characterMetadata.replaceChildren();
    for (const character of characters) {
        const heading = document.createElement('h3');
        heading.textContent = `${character} · U+${character.codePointAt(0).toString(16).toUpperCase().padStart(4, '0')}`;
        const list = document.createElement('dl');
        for (const [field, raw] of characterFields.get(character)) {
            if (field !== 'kDefinition' && !fields.includes(field)) continue;
            let value = raw;
            if (field === 'kSMSZD2003Readings') {
                value = raw.split(/\s+/u).map(pair => {
                    const parts = pair.split('粵');
                    return selectedLanguage() === 'zh-HK' ? parts[1] : parts[0];
                }).filter(Boolean).join(' ');
                if (!value) continue;
            }
            const term = document.createElement('dt');
            term.textContent = field;
            const description = document.createElement('dd');
            description.textContent = value;
            list.append(term, description);
        }
        const empty = document.createElement('p');
        empty.textContent = 'No Unihan metadata for this character in the selected language.';
        characterMetadata.append(heading, list.childElementCount ? list : empty);
    }
    if (!characters.length) characterMetadata.textContent = 'Enter text to inspect character metadata.';
}

diagnostics.addEventListener('toggle', showTimings);
window.addEventListener('load', () => setTimeout(showTimings, 0), { once: true });
metadata.addEventListener('toggle', renderMetadata);

try {
    document.querySelector('#runtime').textContent = runtimeName;
    status.textContent = `Starting ${runtimeName}…`;
    const links = document.querySelectorAll('nav a');
    const isNetWasm = runtimeName === 'NetWasm';
    links[0].href = isNetWasm ? '../' : './';
    links[1].href = isNetWasm ? './' : './netwasm/';
    // Start network requests before runtime initialization, not after it.
    const [interop, wordBytes, characterBytes] = await Promise.all([
        measure('Runtime initialization', createInterop),
        download('./word-readings.tsv', 'Word dictionary download'),
        download('./Unihan_Readings.txt', 'Unihan download')
    ]);
    unihanBytes = characterBytes;
    await measure('Word dictionary indexing', () => interop.LoadDictionaryChunk(wordBytes));
    let loadedReading;

    async function loadReading(event) {
        ++requestId;
        controls.disabled = true;
        try {
            const wordLanguage = language.selectedOptions[0].dataset.language;
            if (wordLanguage) {
                if (event?.target === strategy && strategy.value === 'word') language.value = wordLanguage;
                else strategy.value = 'unihan';
            }
            if (language.value === 'ar') strategy.value = 'word';
            document.querySelector('#alignment-setting').hidden = selectedLanguage() !== 'ja-JP';
            const reading = language.selectedOptions[0].dataset.reading;
            if (strategy.value === 'unihan' && loadedReading !== reading) {
                status.textContent = 'Loading Unihan character readings…';
                await measure(`Unihan indexing · ${reading}`, () => interop.LoadUnihanDictionary(unihanBytes, reading));
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
            language: language.selectedOptions[0].dataset.language ?? language.value,
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
            renderMetadata();
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
        text.value = selectedLanguage() === 'ar' ? 'كتب'
            : selectedLanguage() === 'za' ? '𬺣'
            : selectedLanguage() === 'ja-JP' ? '山林'
            : ['zh-CN', 'zh-HK'].includes(selectedLanguage()) ? '你好'
            : '的';
        render();
    });
    document.querySelector('#text-file').addEventListener('change', async event => {
        const input = event.target;
        const file = input.files[0];
        if (!file) return;
        ++requestId;
        controls.disabled = true;
        status.textContent = 'Reading local text file…';
        try {
            text.value = await file.text();
            await render();
        } catch (error) {
            status.textContent = `Text import failed: ${error.message}`;
        } finally {
            controls.disabled = false;
            input.value = '';
        }
    });
    dictionary.addEventListener('change', async () => {
        ++requestId;
        controls.disabled = true;
        status.textContent = 'Loading dictionary chunks…';
        try {
            for (const file of dictionary.files) {
                await measure('Uploaded word dictionary indexing', async () =>
                    interop.LoadDictionaryChunk(new Uint8Array(await file.arrayBuffer())));
            }
            strategy.value = 'word';
            const wordLanguage = language.selectedOptions[0].dataset.language;
            if (wordLanguage) language.value = wordLanguage;
            await render();
        } catch (error) {
            status.textContent = `Dictionary load failed (previous chunks retained): ${error.message}`;
        } finally {
            controls.disabled = false;
            dictionary.value = '';
        }
    });

    await loadReading();
    timings.set('Page to first preview (includes overlapping startup work)', performance.now());
    showTimings();
} catch (error) {
    controls.disabled = true;
    status.textContent = `Startup failed: ${error.message}`;
}
}
