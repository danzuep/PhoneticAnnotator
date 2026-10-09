# Diagrams

## 1. High-Level Pipeline Flowchart

This **Flowchart Diagram** illustrates the high-level data transformation pipeline.

```mermaid
flowchart TD
    Input["Raw text: 大人買い"] --> Tokenizer["1. Morphological tokenizer"]
    Dictionaries["UniDic, IPADic, JMDict"] -.-> Tokenizer
    Tokenizer -->|text and reading| Aligner["2. Character aligner"]
    Rules["Okurigana and jukujikun rules"] -.-> Aligner
    Aligner -->|aligned tokens| Filter["3. Filter pipeline"]
    Settings["JLPT and script filters"] -.-> Filter
    Filter -->|filtered AST| Formatter["4. Output formatter"]
    Formatter --> HTML["HTML"]
    Formatter --> JSON["JSON"]
    Formatter --> Anki["Anki"]
```

## 2. Detailed Strategy & Visitor Class Diagram

This **Architecture / Class Structure Diagram** shows how the decoupled design patterns (Strategy, Factory, Visitor) map to these stages.

```mermaid
flowchart LR
    Input["Raw text"] --> Tokenizer["ITokenizerStrategy"]
    Mecab["MecabTokenizer"] -.->|implements| Tokenizer
    JMDict["JMDictTokenizer"] -.->|implements| Tokenizer
    Tokenizer -->|tokens| Aligner["IPhoneticAligner"]
    SpanAligner["SpanCharacterAligner"] -.->|implements| Aligner
    Aligner --> AST["GlossToken / AST"]
    AST --> Filter["IGlossFilter"]
    JLPT["JlptLevelFilter"] -.->|implements| Filter
    Filter --> Visitor["IGlossAstVisitor"]
    Ruby["HtmlRubyVisitor"] -.->|implements| Visitor
    JSON["JsonExporterVisitor"] -.->|implements| Visitor
    Anki["AnkiSyntaxVisitor"] -.->|implements| Visitor
    Visitor --> Output["Formatted output"]
```
