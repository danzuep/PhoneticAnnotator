# Phonetic Annotator

Phonetic Annotator is a .NET 10 library and CLI for adding Mandarin, Cantonese/Jyutping, Japanese, or Arabic pronunciation glosses while preserving Unicode offsets and unresolved readings; it includes Unicode 18.0.0 Unihan character readings, supports caller-supplied word lexicons in `language<TAB>word<TAB>reading1|reading2` TSV format (see `data/examples/word-readings.tsv`), builds with `dotnet build src/PhoneticAnnotator.slnx`, tests with `dotnet test src/PhoneticAnnotator.slnx`, updates Unihan with `pwsh -NoProfile -File scripts/update-unihan.ps1`, and accepts either `--data data/Unihan_Readings.txt` or `--lexicon path/to/readings.tsv` with language, text, and output-format options.

---

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
