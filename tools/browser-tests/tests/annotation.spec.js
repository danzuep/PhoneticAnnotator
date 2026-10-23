import { test, expect } from '@playwright/test';

async function ready(page, path) {
    await page.goto(path);
    await expect(page.locator('#status')).toHaveText('Ready. Text and dictionaries stay in this browser.');
}

async function jsonOutput(page, language, text) {
    await page.locator('#language').selectOption(language);
    await expect(page.locator('#controls')).toBeEnabled();
    await page.locator('#format').selectOption('json');
    await page.locator('#text').fill('');
    await expect.poll(async () => {
        try { return JSON.parse(await page.locator('#preview').innerText()).children.length; }
        catch { return -1; }
    }).toBe(0);
    await page.locator('#text').fill(text);
    await expect.poll(async () => {
        try { return JSON.parse(await page.locator('#preview').innerText()); }
        catch { return null; }
    }).not.toBeNull();
    // Wait until the JSON reflects this input rather than a previous render.
    await expect.poll(async () => {
        const ast = JSON.parse(await page.locator('#preview').innerText());
        return ast.children.map(node => node.baseText ?? node.value).join('');
    }).toBe(text);
    return JSON.parse(await page.locator('#preview').innerText());
}

for (const runtime of [{ path: './', name: '.NET' }, { path: './netwasm/', name: 'NetWasm' }]) {
    test(`${runtime.name}: ruby, JSON, alignment, escaping and atomic chunk loading`, async ({ page }) => {
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await ready(page, runtime.path);
        await expect(page.locator('#pick-first')).toBeChecked();
        await page.locator('#strategy').selectOption('word');
        await expect(page.locator('#controls')).toBeEnabled();
        await expect(page.locator('#preview ruby rt')).toHaveText('た');
        await expect(page.locator('#latency')).toHaveText(/^\d+\.\d{3} ms$/);
        await page.locator('#alignment').uncheck();
        await expect(page.locator('#preview ruby rt')).toHaveText('たべる');

        await page.locator('#pick-first').uncheck();
        const chinese = await jsonOutput(page, 'zh-CN', '你好重庆😀');
        expect(chinese.children.map(node => node.type)).toEqual(['ruby', 'unresolved', 'text']);
        expect(chinese.children[1].readingCandidates).toHaveLength(2);

        await page.locator('#format').selectOption('html');
        await page.locator('#text').fill('<img src=x onerror=alert(1)>你好');
        await expect(page.locator('#preview')).toContainText('<img src=x onerror=alert(1)>');
        await expect(page.locator('#preview img')).toHaveCount(0);

        await page.locator('#dictionary').setInputFiles({
            name: 'words.tsv', mimeType: 'text/tab-separated-values',
            buffer: Buffer.from('zh-CN\t世界\tshì jiè\nzh-CN\t恶\t<img src=x onerror=alert(1)>\n')
        });
        await expect(page.locator('#controls')).toBeEnabled();
        await page.locator('#text').fill('你好世界恶');
        await expect(page.locator('#preview ruby')).toHaveCount(3);
        await expect(page.locator('#preview rt').nth(2)).toHaveText('<img src=x onerror=alert(1)>');
        await expect(page.locator('#preview img')).toHaveCount(0);

        await page.locator('#dictionary').setInputFiles({
            name: 'invalid.tsv', mimeType: 'text/tab-separated-values',
            buffer: Buffer.from('zh-CN\t坏\tbad\ninvalid\n')
        });
        await expect(page.locator('#status')).toContainText('Dictionary load failed');
        const preserved = await jsonOutput(page, 'zh-CN', '你好世界坏');
        expect(preserved.children.map(node => node.type)).toEqual(['ruby', 'ruby', 'text']);

        await page.locator('#format').selectOption('html');
        await page.locator('#language').selectOption('ar');
        await page.locator('#text').fill('كتب');
        await expect(page.locator('#preview .phonetic-annotation .reading')).toHaveText('كَتَبَ');
        await page.setViewportSize({ width: 375, height: 812 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        expect(errors).toEqual([]);
    });

    test(`${runtime.name}: all modern Unihan fields and ambiguity toggle`, async ({ page }) => {
        await ready(page, runtime.path);
        for (const [language, expected] of [
            ['zh-CN', 'de'], ['zh-HK', 'dik1'], ['ja-JP', 'テキ'],
            ['ko', '적'], ['vi', 'đích'], ['za', 'guh'],
            ['kJapaneseOn', 'TEKI'], ['kJapaneseKun', 'MATO'], ['kKorean', 'CEK'],
            ['kHanyuPinlu', 'de'], ['kHanyuPinyin', 'dì'], ['kTGHZ2013', 'de'],
            ['kXHC1983', 'de'], ['kSMSZD2003Readings', 'dì'],
            ['kSMSZD2003Readings-Cantonese', 'dik1']
        ]) {
            await page.locator('#language').selectOption(language);
            await expect(page.locator('#controls')).toBeEnabled();
            const ast = await jsonOutput(page, language, language === 'za' ? '𬺣😀' : '的😀');
            expect(ast.children[0].glossText, language).toBe(expected);
            expect(ast.children[1].value).toBe('😀');
        }
        await page.locator('#language').selectOption('kHanyuPinyin');
        await expect(page.locator('#controls')).toBeEnabled();
        await page.locator('#pick-first').uncheck();
        const ast = await jsonOutput(page, 'kHanyuPinyin', '的');
        expect(ast.children[0].type).toBe('unresolved');
        expect(ast.children[0].readingCandidates).toEqual(['dì', 'dí', 'de']);
    });
}

test('Both runtimes produce the same AST for all example languages', async ({ browser, baseURL }) => {
    const desktopRuntime = await browser.newPage({ baseURL });
    const netwasm = await browser.newPage({ baseURL });
    try {
        await ready(desktopRuntime, './');
        await ready(netwasm, './netwasm/');
        await desktopRuntime.locator('#strategy').selectOption('word');
        await netwasm.locator('#strategy').selectOption('word');
        for (const [language, text] of [
            ['ja-JP', '食べる😀'], ['zh-CN', '你好重庆𠀀'],
            ['zh-HK', '重庆'], ['ar', 'كتب'], ['ja-JP', '<script>&"\'']
        ]) {
            expect(await jsonOutput(netwasm, language, text))
                .toEqual(await jsonOutput(desktopRuntime, language, text));
        }
    } finally {
        await desktopRuntime.close();
        await netwasm.close();
    }
});
