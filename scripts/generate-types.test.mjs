import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { generateDeclarations, main } from './generate-types.mjs';

const source = await readFile(new URL('../src/PhoneticAnnotator.Wasm/Interop.cs', import.meta.url), 'utf8');
const fixture = signature => `namespace Example.Wasm;
public static partial class Api {
    [JSExport]
    ${signature}
}`;

test('declarations derive every export, its parameters and actual byte[] binding', () => {
    const output = generateDeclarations(source);
    assert.equal(output, generateDeclarations(source));
    assert.match(output, /LoadDictionaryChunk\(dictionaryData: Uint8Array\): Promise<void>/);
    assert.match(output, /AnnotateToHtml\(text: string, optionsJson: string\): Promise<string>/);
    assert.equal((output.match(/Promise</g) ?? []).length, 5);
    const renamed = generateDeclarations(source.replaceAll('AnnotateToHtml', 'RenderMarkup'));
    assert.match(renamed, /RenderMarkup\(text: string/);
    assert.doesNotMatch(renamed, /AnnotateToHtml/);
    assert.match(output, /PhoneticAnnotator: \{ Wasm: \{ Interop: PhoneticAnnotatorInterop \}\};/);
    assert.equal((output.match(/{/g) ?? []).length, (output.match(/}/g) ?? []).length);
});

test('unsupported exports fail instead of silently producing inaccurate declarations', () => {
    for (const signature of [
        'public static string Render(string text) => text;',
        'public static Task<int> Render(string text) => default;',
        'public static Task Render(int value) => default;',
        'public static Task Render(byte[] data, string name = "") => default;',
        'public static Task Render([JSMarshalAs<JSType.Array<JSType.Number>>] byte[] data) => default;',
        'public static Task Render(ref string value) => default;'
    ]) assert.throws(() => generateDeclarations(fixture(signature)), /Unsupported/);
    assert.throws(() => generateDeclarations(fixture('public static Task Render() => default;')
        .replace('[JSExport]', '[JSExport("custom")]')), /Unsupported export attribute/);
    assert.throws(() => generateDeclarations(fixture('public static Task Render() => default;')
        .replace('}', '[JSExport] public static Task Render(string text) => default; }')), /Overloaded/);
    assert.throws(() => generateDeclarations(fixture('').replace('[JSExport]', '')), /No \[JSExport\]/);
});

test('commented exports are ignored, and generated output is current', async () => {
    assert.equal(generateDeclarations(`${source}\n// [JSExport] unsupported\n/* [JSExport] */`), generateDeclarations(source));
    await main(['--check']);
    await assert.rejects(main(['--unknown']), /Usage:/);
});
