# Phonetic Annotator

Phonetic Annotator is a .NET 10 library with browser-only deployments for adding pronunciation glosses while preserving Unicode offsets and unresolved readings. Both WebAssembly demos include Unicode 18.0.0 Unihan character readings for Mandarin, Cantonese/Jyutping, Japanese, Korean, Vietnamese, and Zhuang, plus a word-lexicon strategy supporting those languages and Arabic. Caller-supplied lexicons use `language<TAB>word<TAB>reading1|reading2` TSV format (see `data/examples/word-readings.tsv`). The CLI remains a developer utility, not a supported desktop deployment target. Build with `dotnet build src/PhoneticAnnotator.slnx`, test with `dotnet test src/PhoneticAnnotator.slnx`, and update Unihan with `pwsh -NoProfile -File scripts/update-unihan.ps1`.

---

## Native .NET 10 WebAssembly demo

`src/PhoneticAnnotator.Wasm` uses `Microsoft.NET.Sdk.WebAssembly`, `browser-wasm`,
and direct `[JSExport]` / `[JSImport]` interop. It has no Blazor or ASP.NET Core
runtime dependency. Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet workload install wasm-tools
dotnet tool install --global dotnet-serve
dotnet build src/PhoneticAnnotator.slnx
dotnet test src/PhoneticAnnotator.Tests/PhoneticAnnotator.Tests.csproj -c Release
dotnet publish src/PhoneticAnnotator.Wasm/PhoneticAnnotator.Wasm.csproj -c Release -f net10.0
dotnet serve --address 127.0.0.1 --port 8000 --directory src/PhoneticAnnotator.Wasm/bin/Release/net10.0/publish/wwwroot
```

Open <http://localhost:8000>. Release publishing enables AOT compilation, trimming,
SIMD, and invariant globalization (Unicode input remains supported); the first
publish requires the native WASM toolchain and takes longer than an ordinary build.
For a quicker interpreter-only publish, append `-p:RunAOTCompilation=false`.
Serve the generated **publish/wwwroot**, not the source `demo` directory or its
parent `publish` directory. All module/dictionary URLs are relative, including on project Pages sites.

### JavaScript API and dictionaries

`demo/app.mjs` creates the runtime, registers the `phonetic-demo.onReady` import,
gets exports with `getAssemblyExports('PhoneticAnnotator.Wasm')`, and awaits
`runMain(getConfig().mainAssemblyName, [])`. The async C# entry point initializes
the in-memory annotation session before invoking `onReady`. The methods on
`exports.PhoneticAnnotator.Wasm.Interop` return JavaScript promises:

- `AnnotateToHtml(text, optionsJson)` returns encoded HTML from `HtmlRubyVisitor`.
- `AnnotateToJson(text, optionsJson)` returns the Core discriminated AST JSON,
  including unresolved reading candidates. JSON rendering is reflection-free and
  safe under trimming/AOT.
- `LoadUnihanDictionary(bytes, readingId)` parses the selected reading field from
  UTF-8 `Unihan_Readings.txt` and replaces the single selected immutable Unihan
  dictionary using `Interlocked.Exchange`. It does not accumulate fields. Failed
  loads leave the previous dictionary unchanged; in-flight annotations retain their
  captured snapshot. Await the selected dictionary load before annotating with it.
- `LoadDictionaryChunk(uint8Array)` adds one **complete UTF-8 word-lexicon TSV**
  chunk: `language<TAB>word<TAB>reading1|reading2`, with optional comments/blank
  lines. Chunks are files, not fragments of a line or arbitrary Unihan/binary data.
  Chunks parse independently and publish atomically using `Interlocked.CompareExchange`;
  malformed chunks leave the existing dictionary unchanged. Concurrent publications
  retry against the latest snapshot, so successful chunks are never lost.
  Annotations capture immutable snapshots, so loads never mutate in-flight work.
  Longest words win across chunks; the last published chunk wins equal-length ties.
  Concurrent calls publish in completion order, not invocation order. Await each
  load in sequence when override order matters (as the demo's file picker does).
  For efficiency, batch entries into a few substantial chunks rather than many tiny
  files (lookup cost grows with the number of chunks).

Options are a JSON object:

- `strategy`: `"word"` (API default) or `"unihan"` (demo default).
- `readingSystem`: a Unihan field ID from the list below; use
  `kSMSZD2003Readings-Cantonese` for SMSZD Cantonese and `kSMSZD2003Readings`
  for its Mandarin mode. `ar` selects Arabic word readings.
- `language`: legacy case-insensitive mappings `ja-JP` → `kJapanese` (API default),
  `zh-CN` → `kMandarin`, `zh-HK` → `kCantonese`, `ko` → `kHangul`,
  `vi` → `kVietnamese`, `za` → `kZhuang`, and `ar`/`ar-Arab` → Arabic.
  With Unihan, an explicit `readingSystem` selects the reading and associated language instead.
  Word mode requires the matching language and its default reading system; alternate
  Unihan fields cannot relabel word-dictionary readings. Switching to word mode in the
  demo selects that language's default system automatically.
- `pickFirstReading`: boolean, default `false` in the API and `true` in the demo.
  Selecting the first candidate is a heuristic, **not contextual disambiguation**.
  Disable it to preserve ambiguous candidates as unresolved readings.
- `alignOkurigana`: boolean, default `true`.

The full modern Unihan demo offers `kMandarin`, `kCantonese`, `kJapanese`,
`kJapaneseOn`, `kJapaneseKun`, `kHangul`, `kKorean`, `kVietnamese`, `kZhuang`,
`kHanyuPinlu`, `kHanyuPinyin`, `kTGHZ2013`, `kXHC1983`, and
`kSMSZD2003Readings` with separate Mandarin/Cantonese modes. Historical `kTang`
and `kFanqie`, non-reading `kDefinition`, and absent `kBopomofo` are excluded.
On first use, the demo lazily downloads the approximately **8.5 MB**
`Unihan_Readings.txt` file and caches its bytes in browser memory. Only the selected
field is parsed; switching fields reuses the cached download and replaces the
selected dictionary. The cache is not persistent across page reloads.

The word strategy remains available with the small example
`data/examples/word-readings.tsv` and additional TSV uploads through the file picker;
it is not a full morphological dictionary. Unknown text is preserved; an empty word
dictionary is valid. Unihan annotation requires the matching dictionary to be loaded.
Rejected promises expose invalid input/options/dictionary errors.
The latency badge measures the annotation call, including interop, but excludes
runtime/dictionary startup and DOM rendering.

Strings and `byte[]` necessarily copy across the supported JS interop boundary:
`[JSExport]` cannot directly marshal `ReadOnlySpan<char>` or
`ReadOnlyMemory<byte>`, and this API does **not** claim end-to-end zero allocation.
Internally, tokenization slices the original `ReadOnlyMemory<char>` and performs
`ReadOnlySpan<char>` trie/rune lookups without substring copies or boxing; dictionary
loading views the marshaled bytes as `ReadOnlyMemory<byte>` and wraps the same
array in a read-only `MemoryStream` without cloning it. AST nodes, decoded dictionary
entries, options, and returned strings still allocate. Shared dictionaries are
immutable, and each call owns its visitors/options/AST. The default runtime is
single-threaded and does not require cross-origin isolation headers.

### Alternative NetWasm browser runtime

The same Unihan and word-lexicon pipeline also targets **`netwasm0.1`**, using
[NetWasm](https://github.com/zion-sati/NetWasm) SDK **0.10.2** and its
**0.10.0** JSON/LINQ library ports. This is a separate compiler/runtime, not
Microsoft's `browser-wasm` target. `PhoneticAnnotator.Core` multi-targets
`net10.0;netwasm0.1`; `PhoneticAnnotator.NetWasm` publishes the alternate
`JsModule` host. Both SPAs load `<script type="module" src="app.mjs"></script>`
and share the UI, dictionary parsing, async token stream, alignment, AST, and visitors.

```sh
dotnet publish src/PhoneticAnnotator.NetWasm/PhoneticAnnotator.NetWasm.csproj -c Release -o artifacts/netwasm
dotnet serve --address 127.0.0.1 --port 8001 --directory artifacts/netwasm
```

Open <http://localhost:8001>. The module opens the generated
`browser/PhoneticAnnotator.NetWasm.mjs` without `dotnet.js`. NetWasm requires
HTTPS or localhost for startup integrity checks. It does not need `wasm-tools`;
the SDK restores its own pinned host tools. Use .NET SDK 10.0.303 or later.

Compatibility boundaries:

- Both demos parse the bundled UTF-8 `Unihan_Readings.txt` for the selected
  modern reading field and accept complete UTF-8 word-lexicon TSV chunks.
  The browser API does not accept Unihan ZIP archives.
- NetWasm 0.10.2 cannot export `Task<string>`. Its facade exports `Task` and
  delivers each string through an explicit `[JSImport]` callback keyed by request
  ID. `app.mjs` exposes the same promise-returning UI API without blocking waits.
  Both runtimes share the same lock-free dictionary publication code without an
  extra JavaScript queue; rejected chunks do not poison later loads.
- NetWasm does not expose `HtmlEncoder`. Its target-specific visitor encoding
  escapes `&`, `<`, `>`, both quote characters, and preserves Unicode; the .NET 10
  target uses `HtmlEncoder.Default`. Both source and dictionary text are encoded.
- NetWasm has no managed threading/shared heap. `Interlocked` follows its
  single-reactor semantics there and provides atomic publication on .NET 10.
  Annotations use a volatile read of one immutable snapshot for their entire call.
- NetWasm is pre-1.0 and has separate compiler/tooling licensing; review the
  upstream license before commercial adoption. Strings/arrays still copy at its
  JavaScript boundary; no zero-allocation or universal near-native-speed claim
  is made.

### CI and GitHub Pages

1. Merge the workflow onto the default branch and enable **Settings → Pages →
   Build and deployment → Source: GitHub Actions** (requires repository admin access).
2. Push to `main`, or open **Actions → Browser WebAssembly and Pages → Run workflow**
   and select the branch to deploy. Manual dispatch can deploy any branch, subject
   to the `github-pages` environment's deployment rules. Pull requests and other
   branch pushes build/test without deploying Pages.
3. Approve the `github-pages` environment deployment if your repository requires it.
   The deploy job exposes the resulting Pages URL, normally
   `https://<owner>.github.io/PhoneticAnnotator/`.

Deployments support browsers only; there is no native deployment matrix.
A single `browser-build` job runs managed tests once, publishes both browser runtimes, and uploads
the combined `phoneticannotator-browser-site` artifact. Each browser matrix job
downloads that same site, without rebuilding or installing WASM build tools.
It tests **both** browser runtimes in Chromium, Firefox, and WebKit,
including AST parity, alignment, dictionary uploads/recovery, and HTML escaping.
The combined site serves Microsoft WASM at the root and NetWasm at `/netwasm/`,
with runtime links in the UI. Pages deployment waits for the entire browser
matrix, deploys the shared site tested by Chromium, skips stale revisions by
checking the current run's `GITHUB_REF` (including manually selected branches),
and runs Chromium against both live pages. Browser reports are uploaded separately
without duplicating the site in each test artifact.
Only the deployment job receives Pages write/OIDC permissions.
Agent-authored workflow runs may require a maintainer's **Approve and run**
before any jobs start; no Pages deployment occurs until those checks complete.

To reproduce browser checks locally, publish both runtimes, assemble the same
project-path site used by CI, then run:

```sh
dotnet publish src/PhoneticAnnotator.Wasm/PhoneticAnnotator.Wasm.csproj -c Release -f net10.0
dotnet publish src/PhoneticAnnotator.NetWasm/PhoneticAnnotator.NetWasm.csproj -c Release -o artifacts/netwasm
mkdir -p artifacts/site/PhoneticAnnotator/netwasm
cp -a src/PhoneticAnnotator.Wasm/bin/Release/net10.0/publish/wwwroot/. artifacts/site/PhoneticAnnotator/
cp -a artifacts/netwasm/browser artifacts/netwasm/index.html artifacts/netwasm/app.mjs artifacts/netwasm/main.js artifacts/netwasm/word-readings.tsv artifacts/netwasm/Unihan_Readings.txt artifacts/netwasm/THIRD-PARTY-NOTICES.txt artifacts/site/PhoneticAnnotator/netwasm/
cd tools/browser-tests
npm ci
npx playwright install --with-deps
npm test
```

Install the local server once with `dotnet tool install --global dotnet-serve`
(CI pins version `1.10.194`). The test runner starts `dotnet serve` under
`/PhoneticAnnotator/`, bound to loopback only.
To test an already hosted combined site instead, set `PHONETIC_BASE_URL` to its
HTTPS URL **ending in `/`**; it then skips the local server. Test traces/reports
are written under `artifacts/`. No audio, desktop GUI, game fixtures, or unrelated
Doom checks from the example workflow are required.

## Typical Data Flow

~~~
[ Raw Japanese Text ]
         │
         ▼
[ 1. Morphological Tokenizer ]  ◄── (UniDic / IPADic / JMDict)
         │  (Token: "大人買い", Reading: "おとながい")
         ▼
[ 2. Character Aligner ]        ◄── (Okurigana heuristics & Jukujikun maps)
         │  (Pairs: [大人: おとな], [買: が], [い: -])
         ▼
[ 3. Filter Pipeline ]          ◄── (User Settings: JLPT levels, scripts)
         │
         ▼
[ 4. Output Formatter ]         ──► HTML (<ruby>) / JSON / Anki Syntax
~~~
