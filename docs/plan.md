# Implementation Plan

## Completed

- [x] Create the .NET 10 solution, Core library, CLI, and xUnit test project.
- [x] Define language, reading-system, source-backed token, tokenizer, and character-reading provider contracts.
- [x] Adapt the local indexed Unihan lookup to provide field-specific reading candidates.
- [x] Add a Rune-based fallback tokenizer that preserves source slices, UTF-16 offsets, input order, malformed surrogate text, and cancellation.
- [x] Resolve only single-candidate readings; preserve ambiguous candidates instead of guessing.
- [x] Assemble owned document, ruby, plain-text, and unresolved AST nodes; define a paired-pronunciation node for future language providers.
- [x] Render HTML ruby with encoded text and Anki-style `base[reading]` output with delimiter escaping.
- [x] Serialize all AST node types to JSON with language, reading-system, and source-field metadata.
- [x] Define a zero-allocation span alignment API with stackalloc-friendly offset segments and explicit destination-capacity behavior.
- [x] Connect alignment output to AST assembly while keeping span processing synchronous.
- [x] Add conservative Japanese okurigana alignment with hiragana/katakana matching and whole-token fallback when uncertain.
- [x] Cover supplementary-plane offsets, malformed UTF-16, cancellation, field selection, ambiguity, and rendering with focused tests.
- [x] Verify the loaded Unihan -> language strategy -> AST -> JSON path for Mandarin, Jyutping, and Japanese fixtures.
- [x] Support paired-pronunciation AST output and verify Arabic Tashkeel through an injected fixture strategy.
- [x] Add a CLI workflow that reads a caller-supplied Unihan stream and renders HTML, Anki, or JSON.
- [x] Preserve the third-party MIT notice for the local Unihan code separately from data licensing.

## Future Work

- [x] Add a strategy factory with language and preferred-strategy registration/resolution.
- [x] Add a cancellation-aware Unihan stream loader that filters requested fields without taking ownership of the caller's stream.
- [x] Accept both release-format Unihan rows and draft-review rows with the ideograph in the first column.
- [x] Copy Unicode 16.0.0 `Unihan_Readings.txt` into `data/`, preserve its original license header, and use it in the CLI workflow.
- [ ] Review property-specific source terms, including Japanese readings, before redistributing any derived data.
- [ ] Optionally add https://github.com/unicode-org/unihan-database as a Git submodule for reviewing provisional property updates; it is not a complete release snapshot. Keep ordinary builds working without initializing the submodule.
- [ ] Expand language-specific partial alignment policies for broader Japanese and mixed-script text; keep spans out of async state and retained AST data.
- [ ] Add provider-backed word tokenization and contextual disambiguation for Japanese, Mandarin, and Cantonese/Jyutping.
- [ ] Select and integrate a production Arabic Tashkeel provider/data source; the current test uses an injected fixture only.
