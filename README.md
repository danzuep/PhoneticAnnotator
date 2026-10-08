# Phonetic Annotator

Phonetic Annotator is a .NET 10 library foundation for adding pronunciation glosses to text: its current vertical slice adapts indexed Unihan readings, tokenizes Unicode scalars while preserving UTF-16 source offsets, keeps ambiguous readings unresolved, assembles an AST, and renders HTML ruby or Anki-style output. Build by running `dotnet build src/PhoneticAnnotator.slnx`, and run tests with `dotnet test src/PhoneticAnnotator.slnx`.
