# Phonetic Annotator

Phonetic Annotator is a .NET 10 library with browser-only deployments for adding pronunciation glosses while preserving Unicode offsets and unresolved readings. Both WebAssembly demos include Unicode 18.0.0 Unihan character readings for Mandarin, Cantonese/Jyutping, Japanese, Korean, Vietnamese, and Zhuang, plus a word-lexicon strategy supporting those languages and Arabic. Caller-supplied lexicons use `language<TAB>word<TAB>reading1|reading2` TSV format (see `data/examples/word-readings.tsv`). The CLI remains a developer utility, not a supported desktop deployment target. Build with `dotnet build src/PhoneticAnnotator.slnx`, test with `dotnet test src/PhoneticAnnotator.slnx`, and update Unihan with `pwsh -NoProfile -File scripts/update-unihan.ps1`.

---

## Native .NET 10 WebAssembly demo

`src/PhoneticAnnotator.Wasm` uses `Microsoft.NET.Sdk.WebAssembly`, `browser-wasm`,
and direct `[JSExport]` / `[JSImport]` interop. It has no Blazor or ASP.NET Core
runtime dependency. Install the .NET 10 SDK and Node.js 24 (for declaration generation),
then run from the repository root:

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
During startup, both demos preload the example word dictionary and approximately **8.5 MB**
`Unihan_Readings.txt` file concurrently with runtime initialization, and cache their bytes in browser memory. Only the selected
field is parsed; switching fields reuses the cached download and replaces the
selected dictionary. Bundled dictionary responses are also persisted in an
application-scoped browser Cache Storage cache across page reloads.

### Persistent dictionary downloads

Both demos use `dictionary-cache.js` for bundled word and Unihan downloads.
Online loads revalidate cached responses with ETag/Last-Modified when available;
servers without validators return a fresh download. Changed responses replace the
persisted copy. Network failures can reuse a previously cached dictionary, with
the offline source shown in diagnostics; HTTP errors are not silently hidden by
stale data. This caches downloaded UTF-8 bytes, **not** the managed dictionary
index, so a new runtime still parses/indexes the bytes.

Cache Storage is best-effort: private browsing, insecure origins, quota limits,
or browser eviction may disable persistence without preventing online annotation.
Use HTTPS or localhost for reliable access. The demo's clear-cache control removes
only this application's dictionary entries, not unrelated origin caches. Clearing
download storage does not unload the current in-memory dictionary. Uploaded
dictionaries and annotation text are never persisted by this feature.

The word strategy remains available with the small example
`data/examples/word-readings.tsv` and additional TSV uploads through the file picker;
it is not a full morphological dictionary. Unknown text is preserved; an empty word
dictionary is valid. Unihan annotation requires the matching dictionary to be loaded.
Rejected promises expose invalid input/options/dictionary errors.
The latency badge measures the annotation call, including interop, but excludes
runtime/dictionary startup and DOM rendering.

Both demos start with Mandarin/Pinyin and the unambiguous example 你好. The Japanese
example is 山林, whose first character readings give サンリン, rather than using
食べる with the character-reading heuristic. Japanese alignment controls are hidden
for other languages. Import UTF-8 `.txt` or `.md` files locally using the text picker;
neither text nor dictionaries are sent to a server.

The collapsed **Character metadata** panel shows raw fields and all alternatives
for each distinct non-whitespace input character, filtered to the selected language,
plus language-neutral definitions. Combined SMSZD data is split into its Mandarin
or Cantonese part. Historical Chinese fields are visible here but are not used for
annotation. Missing metadata is explicit; this panel describes bundled Unihan data,
not uploaded word entries. Raw metadata is decoded only when the panel is opened.

The collapsed **Page and dictionary load diagnostics** panel separates document,
runtime initialization, dictionary fetch/indexing, and first-preview timings, and
lists browser resource transfer/encoded/decoded sizes. Fetches overlap runtime
startup, so these durations are not additive. The separate runtime URLs already
isolate initialization while reusing the same markup, styles, and controller.
Compare cold-cache and warm-cache visits separately with the same reading selected.
A smaller NetWasm bundle only reduces potential transfer time: module compilation,
integrity checks, runtime initialization, interop copies, and managed dictionary
parsing also contribute. Microsoft Release builds use AOT; NetWasm uses a different
compiler/runtime. Package size alone cannot establish which phase is slower;
use the diagnostics to identify the bottleneck on the actual browser/device.

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

### Background worker and TypeScript

The published browser directory includes `PhoneticAnnotator.d.ts` and
`phonetic-annotator-worker.js`. Create the latter as a **module Web Worker**;
it loads `./_framework/dotnet.js` relative to its own location, not the page.
Keep the worker beside `_framework`, or send an `initialize` request with
`args: [{ runtimeUrl: "/your-engine/_framework/dotnet.js" }]` before other calls.
Runtime URLs must point to trusted application assets.

Send `{ id, method, args }`, using any unique string or finite numeric ID.
Methods match the four .NET exports listed above; `args` is their ordered argument
array. Dictionary bytes are `Uint8Array` values. Await the matching
`{ id, ok: true, result }` response, or handle `{ id, ok: false, error: { name, message } }`.
The worker also emits `{ type: "ready" }` or `{ type: "startup-error", error }`.
Requests execute sequentially, including initialization and dictionary updates;
one failed request does not discard subsequent requests. There is no cancellation
protocol: terminate the worker to abandon all work and release its dictionaries.
Use transferable buffers only when the caller no longer needs the dictionary bytes.

`node scripts/generate-types.mjs` regenerates declarations from `[JSExport]`
signatures. `node scripts/generate-types.mjs --check` detects stale output.
The WASM build also generates them. Strings, serialized AST results, and dictionary
interop still allocate/copy; running in a worker avoids main-thread computation,
not message serialization costs.

### Synchronous processing and AST filters

#### Fluent .NET API

Use `AnnotationEngine` as the typed entry point instead of assembling low-level
tokenizers, disambiguators, and visitors manually. Fluent methods and nested
`Action<TBuilder>` callbacks configure the same immutable engine:

```csharp
using PhoneticAnnotator.Core;

// Load once; streams remain caller-owned.
var lexicon = await WordReadingLexicon.LoadAsync(dictionaryStream);
var engine = AnnotationEngine.Create(builder => builder
    .Reading("kJapanese") // Selects the catalog language and reading system.
    .PickFirstReading()
    .Dictionaries(dictionaries => dictionaries.Words(lexicon))
    .Filters(filters => filters
        .KnownWords(["猫"])
        .Script(JapaneseScript.Romaji)));

var document = engine.Annotate("猫と食べる");
var html = engine.ToHtml(document);
var json = engine.ToJson(document); // Render the same AST without tokenizing again.
```

Alternatively, instantiate `AnnotationEngineBuilder`, chain configuration calls,
and call `Build()`. Built engines snapshot dictionary/filter collections; later
builder changes do not modify earlier engines. Reuse an engine for repeated calls.
`Language(...)` plus `Reading(ReadingSystem)` configures a typed reading explicitly;
`Dictionaries(d => d.Unihan(dictionary))` selects scalar Unihan matching instead of
word dictionaries. Mixing scalar and word sources is rejected rather than silently
ignoring one. Dictionary loading stays asynchronous and explicit; annotation is
synchronous over borrowed input and returns an owned document.

Japanese kana uses automatic okurigana alignment; `WithoutAlignment()` keeps
whole-token readings and `Alignment(customAligner)` selects a language-compatible
aligner. Filters execute in registration order: `Jlpt(...)`, `KnownWords(...)`,
`KnownKanji(...)`, `Script(...)`, or `Add(customFilter)`. Configuration validates
language/reading and aligner compatibility at build time. An empty dictionary
configuration preserves unknown text rather than inventing readings.

`ToHtml`, `ToJson`, and `ToAnki` create a fresh visitor for each rendering.
`Render(document, () => new CustomVisitor())` supports custom output while keeping
tokenization strategies independent of visitors. Custom filters and aligners are
shared by reference and must be immutable/thread-safe when the engine is shared;
visitor factories must return a new visitor each time. Custom filters may replace
the root; non-document results are wrapped in a document, and null results fail.
Both annotation and filter execution honor cancellation between processing steps.
The WASM JSON boundary uses the same typed engine internally; JavaScript does not
need to instantiate C# builders.

`TokenEnumerator` borrows a `ReadOnlySpan<char>` and immutable lexicon snapshots.
Its value tokens avoid async state machines and per-token heap records.
`WordLexiconTokenizerStrategy.Tokenize` exposes this path;
`GlossDocumentAssembler.Assemble` copies tokens into an owned AST.
The asynchronous strategy APIs remain available for existing consumers.
SIMD-backed `SearchValues<char>` checks accelerate BMP script boundaries; scalar
decoding still preserves supplementary characters and malformed UTF-16.
`SpanCharacterAligner` uses stack scratch for short readings and pooled character
buffers for long readings, returning rentals in `finally` blocks. Jukujikun and
ambiguous kana boundaries conservatively remain whole-token annotations.
It does not infer readings absent from the dictionary or modernize historical kana.
AST nodes, owned strings, rendering, and options parsing are intentionally not
zero-allocation.

Apply a `GlossFilterPipeline` **after assembly and before invoking any visitor**.
Filters recursively transform immutable documents, sharing unchanged nodes:

- `JlptLevelFilter` accepts a target N1–N5 integer and a caller-supplied
  `IReadOnlyDictionary<Rune, int>` kanji-level mapping. N5 is easiest:
  target N4 suppresses N5 and N4 kanji only when all kanji are mapped.
  No official JLPT dataset is bundled.
- `UserKnownKanjiFilter` defaults to an empty/no-op knowledge set; accepts known
  words and kanji sets, or a `BitArray` indexed by Unicode scalar value.
- `ScriptFilter` selects `JapaneseScript.Hiragana`, `Katakana`, or `Romaji`.
  Romaji uses conservative Hepburn-style transcription, written vowel sequences
  rather than macrons, and apostrophes for ambiguous moraic n. Unsupported
  conversions retain the original annotation rather than inventing readings.

Japanese-specific filters leave other languages unchanged; the filter contract and
pipeline support any language without coupling tokenization to output visitors.
Thai word annotation accepts caller-supplied `th` or `th-TH` TSV lexicons and
`readingSystem: "th"`; this is dictionary matching, not a bundled Thai morphological
segmenter or pronunciation model.

### Benchmarks and release assets

Run the BenchmarkDotNet suite in Release:

```sh
dotnet run --project benchmarks/PhoneticAnnotator.Benchmarks -c Release -- --filter '*'
```

`TextProcessingBenchmarks` compares span and asynchronous tokenization/AST
assembly for Japanese, Chinese, Korean, and Thai using tweet-, article-, and
chapter-sized synthetic inputs. `MemoryDiagnoser` reports allocated bytes/op
alongside timing; setup/dictionary indexing are excluded. `AlignmentBenchmarks`
separately exercises successful unique-kana alignment, whole-compound alignment,
and conservative ambiguous-run fallback over short and long inputs.
Use `--job Dry` only for smoke checks, not performance conclusions. Native .NET
measurements do not predict WASM latency; profile the published worker on target
browsers and devices before claiming browser speedups.

#### Browser and device profiling

Open `profile.html` beside the published native .NET WASM demo to run measurements
on the actual browser/device. Use the **same Release AOT build** when comparing
devices and enter a build/device label. Keep the tab foregrounded, close unrelated
workloads, and record power/thermal conditions separately. The page runs locally
and downloads JSON results; it does not send results, source text, or dictionaries
to a profiling service.

The harness creates a fresh worker for each dictionary-cold and dictionary-warm
pass, measuring runtime startup, dictionary loading/revalidation, and indexing
separately. It measures sequential worker round trips for synthetic tweet,
article, and chapter inputs in Japanese, Chinese, Korean, and Thai, including
interop and JSON serialization. Word dictionaries and supported Unihan readings
are exercised; missing word fixtures use explicitly labelled in-memory
supplements. Warmups and correctness probes are excluded from median/p95
latencies. Raw samples, throughput, dictionary provenance/fingerprints, and
browser/device metadata accompany each report. Download/index timings are single
observations, not statistical distributions.

“Cold” clears only this application's persistent dictionary cache, **not** browser
HTTP caches, compiled WASM caches, or OS caches. “Warm” means a subsequent load;
the reported dictionary source is authoritative (a server without validators may
still return a full response). No fixed performance thresholds are imposed.

For repeatable automation against the assembled site:

```sh
npm --prefix tools/browser-tests ci
npm --prefix tools/browser-tests run profile -- --project=chromium --project=mobile-chromium
```

Set `PHONETIC_BASE_URL` to profile an already-served publication; otherwise the
existing local static-site server is used. Profiling is a separate Playwright
suite, not part of ordinary regression tests. JSON reports are attached under
`artifacts/profiling-results`, with an HTML report under
`artifacts/profiling-report`. The Pages workflow can run it via the optional
`profile` dispatch input.

Mobile Chromium/WebKit profiles are **emulation**, not physical phone benchmarks.
For physical devices, serve the identical publication over HTTPS, open
`profile.html` on each target, and export results there. Ordinary HTTP LAN origins
may lack Cache Storage. This environment cannot establish physical-device results;
do not interpret emulated CI timing as a device-specific performance guarantee.

Publishing a GitHub Release with a semantic-version tag (optionally prefixed `v`)
builds and attaches the Core and Unihan `.nupkg` files, a complete AOT
`PhoneticAnnotator-browser.zip`, and standalone declarations/worker assets.
NuGet registry publication is not automatic; consumers of standalone assets also
need the matching browser runtime bundle.

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
2. Push to `main` or a branch containing `DevOps` (case-insensitive, including
   `copilot/devops-update-loading-perf`), or open **Actions → Browser WebAssembly and Pages → Run workflow**
   and select the branch to deploy. Manual dispatch can deploy any branch, subject
   to the `github-pages` environment's deployment rules. Pull requests build/test
   without deploying Pages. Other branch pushes do not start a workflow;
   open a pull request or dispatch the workflow manually.
3. Approve the `github-pages` environment deployment if your repository requires it.
   The deploy job exposes the resulting Pages URL, normally
   `https://<owner>.github.io/PhoneticAnnotator/`.

Deployments support browsers only; there is no native deployment matrix.
A single `browser-build` job runs managed tests once, publishes both browser runtimes, and uploads
the combined `phoneticannotator-browser-site` artifact. The Chromium check job
downloads that same site, without rebuilding or installing WASM build tools.
It tests **both** browser runtimes in Chromium only,
including AST parity, alignment, dictionary uploads/recovery, and HTML escaping.
The combined site serves Microsoft WASM at the root and NetWasm at `/netwasm/`,
with runtime links in the UI. Pages deployment waits for the Chromium checks,
deploys the shared site tested by Chromium, skips stale revisions by
checking the current run's `GITHUB_REF` (including manually selected branches),
and runs Chromium against both live pages. Browser reports are uploaded separately
without duplicating the site in each test artifact.
Firefox and WebKit are skipped in CI, including their Ubuntu package downloads;
their Playwright projects remain available for optional local checks.
CI installs only Chromium's headless shell, caches npm downloads and build-job NuGet
packages, and skips WASM workload manifest refreshes. New PR revisions cancel
superseded PR runs, while main/manual deployments are not cancelled mid-deploy.
Release AOT and both runtime publishes remain enabled so deployment tests exercise
the actual optimized bundles.
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
npx playwright install --with-deps --only-shell chromium
npm test -- --project=chromium
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
