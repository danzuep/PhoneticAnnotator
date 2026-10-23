# Phonetic Annotator

Phonetic Annotator is a .NET 10 library and CLI for adding Mandarin, Cantonese/Jyutping, Japanese, or Arabic pronunciation glosses while preserving Unicode offsets and unresolved readings; it includes Unicode 18.0.0 Unihan character readings, supports caller-supplied word lexicons in `language<TAB>word<TAB>reading1|reading2` TSV format (see `data/examples/word-readings.tsv`), builds with `dotnet build src/PhoneticAnnotator.slnx`, tests with `dotnet test src/PhoneticAnnotator.slnx`, updates Unihan with `pwsh -NoProfile -File scripts/update-unihan.ps1`, and accepts either `--data data/Unihan_Readings.txt` or `--lexicon path/to/readings.tsv` with language, text, and output-format options.

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

### JavaScript API and dictionary chunks

`demo/app.mjs` creates the runtime, registers the `phonetic-demo.onReady` import,
gets exports with `getAssemblyExports('PhoneticAnnotator.Wasm')`, and awaits
`runMain(getConfig().mainAssemblyName, [])`. The async C# entry point initializes
the in-memory annotation session before invoking `onReady`. The methods on
`exports.PhoneticAnnotator.Wasm.Interop` return JavaScript promises:

- `AnnotateToHtml(text, optionsJson)` returns encoded HTML from `HtmlRubyVisitor`.
- `AnnotateToJson(text, optionsJson)` returns the Core discriminated AST JSON,
  including unresolved reading candidates. JSON rendering is reflection-free and
  safe under trimming/AOT.
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

Options are a JSON object with `language` (`ja-JP`, `zh-CN`, `zh-HK`, `ar`, or
`ar-Arab`; case-insensitive, default `ja-JP`) and `alignOkurigana` (boolean,
default `true`). Unknown text is preserved; an empty dictionary is valid.
Rejected promises expose invalid input/options/dictionary errors.
The UI starts with the small existing `data/examples/word-readings.tsv`; it is not
a full morphological dictionary. Load additional TSV files using the file picker.
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

### Alternative NetWasm runtime and desktop targets

The same word-lexicon pipeline also targets **`netwasm0.1`**, using
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

- NetWasm's Core target includes the shared `UnihanField` enum but not the
  desktop Unihan archive/lookup loaders or character-reading provider. The full
  `net10.0` API and existing Microsoft WASM runtime are unchanged. Both demos use
  complete UTF-8 word-lexicon TSV chunks, not Unihan ZIPs.
- NetWasm 0.10.2 cannot export `Task<string>`. Its facade exports `Task` and
  delivers each string through an explicit `[JSImport]` callback keyed by request
  ID. `app.mjs` exposes the same promise-returning UI API without blocking waits.
  Both runtimes share the same lock-free dictionary publication code without an
  extra JavaScript queue; rejected chunks do not poison later loads.
- NetWasm does not expose `HtmlEncoder`. Its target-specific visitor encoding
  escapes `&`, `<`, `>`, both quote characters, and preserves Unicode; desktop
  continues using `HtmlEncoder.Default`. Both source and dictionary text are encoded.
- NetWasm has no managed threading/shared heap. `Interlocked` follows its
  single-reactor semantics there and provides atomic publication on desktop.
  Annotations use a volatile read of one immutable snapshot for their entire call.
- NetWasm is pre-1.0 and has separate compiler/tooling licensing; review the
  upstream license before commercial adoption. Strings/arrays still copy at its
  JavaScript boundary; no zero-allocation or universal near-native-speed claim
  is made.

Desktop Native AOT uses the existing CLI (replace the RID for another host):

```sh
dotnet publish src/PhoneticAnnotator.Cli/PhoneticAnnotator.Cli.csproj -c Release -r linux-x64 -p:PublishAot=true -o artifacts/linux-x64
artifacts/linux-x64/PhoneticAnnotator.Cli --lexicon data/examples/word-readings.tsv --language ja-JP --text 食べる --format html
```

Native AOT publishing can report IL2026/IL3050 warnings from pre-existing Unihan
reflection-based JSON helper APIs. The CLI annotation path does not call those
helpers; its HTML/JSON word-lexicon smoke checks are exercised separately.

### CI and GitHub Pages

1. Merge the workflow onto the default branch and enable **Settings → Pages →
   Build and deployment → Source: GitHub Actions** (requires repository admin access).
2. Push to `main`, or open **Actions → Desktop and WebAssembly → Run workflow**
   and select `main`. Pull requests and other branch pushes build/test but never
   deploy Pages.
3. Approve the `github-pages` environment deployment if your repository requires it.
   The deploy job exposes the resulting Pages URL, normally
   `https://<owner>.github.io/PhoneticAnnotator/`.

The workflow tests desktop Native AOT on Linux/Windows x64 and ARM64 and macOS
ARM64/x64. A single `browser-build` job publishes both browser runtimes and uploads
the combined `phoneticannotator-browser-site` artifact. Each browser matrix job
downloads that same site, without rebuilding or installing WASM build tools.
It tests **both** browser runtimes in Chromium, Firefox, and WebKit,
including AST parity, alignment, dictionary uploads/recovery, and HTML escaping.
The combined site serves Microsoft WASM at the root and NetWasm at `/netwasm/`,
with runtime links in the UI. Pages deployment waits for the native and browser
matrices, deploys the shared site tested by Chromium, skips stale `main` revisions,
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
cp -a artifacts/netwasm/browser artifacts/netwasm/index.html artifacts/netwasm/app.mjs artifacts/netwasm/main.js artifacts/netwasm/word-readings.tsv artifacts/site/PhoneticAnnotator/netwasm/
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
