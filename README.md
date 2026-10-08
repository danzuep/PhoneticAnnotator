# Phonetic Annotator

Phonetic Annotator is a .NET 10 library and CLI for adding per-character Mandarin, Cantonese/Jyutping, or Japanese Unihan glosses to text, preserving Unicode source offsets and ambiguous readings while rendering HTML ruby, Anki, or JSON; it includes the Unicode 16.0.0 Unihan readings data, builds with `dotnet build src/PhoneticAnnotator.slnx`, tests with `dotnet test src/PhoneticAnnotator.slnx`, and runs with `dotnet run --project src/PhoneticAnnotator.Cli -- --data data/Unihan_Readings.txt --language zh-CN --text "你好" --format json`.
