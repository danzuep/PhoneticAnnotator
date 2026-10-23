# Phonetic Annotator

Phonetic Annotator is a .NET 10 library and CLI for adding Mandarin, Cantonese/Jyutping, Japanese, or Arabic pronunciation glosses while preserving Unicode offsets and unresolved readings; it includes Unicode 18.0.0 Unihan character readings, supports caller-supplied word lexicons in `language<TAB>word<TAB>reading1|reading2` TSV format (see `data/examples/word-readings.tsv`), builds with `dotnet build src/PhoneticAnnotator.slnx`, tests with `dotnet test src/PhoneticAnnotator.slnx`, updates Unihan with `pwsh -NoProfile -File scripts/update-unihan.ps1`, and accepts either `--data data/Unihan_Readings.txt` or `--lexicon path/to/readings.tsv` with language, text, and output-format options.

---

## Native .NET 10 WebAssembly demo

`src/PhoneticAnnotator.Wasm` uses `Microsoft.NET.Sdk.WebAssembly`, `browser-wasm`,
and direct `[JSExport]` / `[JSImport]` interop. It has no Blazor or ASP.NET Core
runtime dependency. Install the .NET 10 SDK and Python 3, then run from the repository root:

```sh
dotnet workload install wasm-tools
dotnet build src/PhoneticAnnotator.slnx
dotnet test src/PhoneticAnnotator.Tests/PhoneticAnnotator.Tests.csproj -c Release
dotnet publish src/PhoneticAnnotator.Wasm/PhoneticAnnotator.Wasm.csproj -c Release -f net10.0
python3 -m http.server 8000 --directory src/PhoneticAnnotator.Wasm/bin/Release/net10.0/publish/wwwroot
```

Open <http://localhost:8000>. Release publishing enables AOT compilation, trimming,
SIMD, and invariant globalization (Unicode input remains supported); the first
publish requires the native WASM toolchain and takes longer than an ordinary build.
For a quicker interpreter-only publish, append `-p:RunAOTCompilation=false`.
Serve the generated **publish/wwwroot**, not the source `demo` directory or its
parent `publish` directory. All module/dictionary URLs are relative, including on project Pages sites.

### JavaScript API and dictionary chunks

`demo/main.js` creates the runtime, registers the `phonetic-demo.onReady` import,
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
  Calls are serialized; malformed chunks leave the existing dictionary unchanged.
  Annotations capture immutable snapshots, so loads never mutate in-flight work.
  Longest words win across chunks; newer chunks override equal-length matches.
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

### Manually deploy to GitHub Pages

1. Merge the workflow onto the default branch and enable **Settings → Pages →
   Build and deployment → Source: GitHub Actions** (requires repository admin access).
2. Open **Actions → Deploy native WASM demo to Pages → Run workflow**, select the
   branch to publish, and run it. This workflow runs only on manual dispatch.
3. Approve the `github-pages` environment deployment if your repository requires it.
   The deploy job exposes the resulting Pages URL, normally
   `https://<owner>.github.io/PhoneticAnnotator/`.

The workflow tests the library, publishes the AOT bundle, and uploads/deploys that
static bundle using Pages' scoped deployment permissions. Subsequent dispatches
replace the site; there is no server component.

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
