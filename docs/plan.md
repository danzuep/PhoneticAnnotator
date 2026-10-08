# Implementation Plan

## Completed

- [x] Create the .NET 10 solution, Core library, CLI, and xUnit test project.
- [x] Define language, reading-system, source-backed token, tokenizer, and character-reading provider contracts.
- [x] Adapt the indexed OpenBible Unihan lookup to provide field-specific reading candidates.
- [x] Add a Rune-based fallback tokenizer that preserves source slices, UTF-16 offsets, input order, malformed surrogate text, and cancellation.
- [x] Resolve only single-candidate readings; preserve ambiguous candidates instead of guessing.
- [x] Assemble owned document, ruby, plain-text, and unresolved AST nodes; define a paired-pronunciation node for future language providers.
- [x] Render HTML ruby with encoded text and Anki-style `base[reading]` output with delimiter escaping.
- [x] Cover supplementary-plane offsets, malformed UTF-16, cancellation, field selection, ambiguity, and rendering with focused tests.

## Future Work

- [ ] Add a strategy factory with language and preferred-strategy registration/resolution.
- [ ] Add an explicit Unihan data-loading/bootstrap path and document the Unicode 16 dataset version used by the current OpenBible assets.
- [ ] Keep Unicode data notices separate from the OpenBible MIT code license; include Unicode License V3 if data is redistributed, and review source terms for the Japanese `kJapanese` field.
- [ ] Add the JSON AST visitor with language, reading-system, and source-field metadata.
- [ ] Define and implement the span alignment API, buffer-capacity behavior, and tests; keep spans out of async state and retained AST data.
- [ ] Add provider-backed word tokenization and contextual disambiguation for Japanese, Mandarin, and Cantonese/Jyutping.
- [ ] Add an Arabic Tashkeel provider and exercise paired-pronunciation rendering end to end.
- [ ] Add end-to-end pipeline tests and a CLI workflow after provider and data-loading contracts are settled.
