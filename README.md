# Phonetic Annotator

Phonetic Annotator is a .NET 10 library and CLI for adding per-character Mandarin, Cantonese/Jyutping, or Japanese Unihan glosses to text, preserving Unicode source offsets and ambiguous readings while rendering HTML ruby, Anki, or JSON; build with `dotnet build src/PhoneticAnnotator.slnx`, test with `dotnet test src/PhoneticAnnotator.slnx`, and run the CLI with a caller-supplied Unicode 18.0.0 UCD archive or `Unihan_Readings.txt` file using `dotnet run --project src/PhoneticAnnotator.Cli -- --data <Unihan.zip> --language zh-CN --text "你好" --format json`.
