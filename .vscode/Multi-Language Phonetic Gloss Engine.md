# Multi-Language Phonetic Gloss Engine Specification

### System Goal

Build a high-performance, modular C# library (.NET 8+) that parses raw text in ideographic/polyphonic languages (Japanese, Chinese, Arabic) and attaches phonetic annotations (Furigana, Pinyin/Zhuyin, Tashkeel). The system must support asynchronous streaming, zero-allocation span parsing, pluggable tokenizer strategies, and visitor-based rendering.

---

### Core Architectural Layers & Design Patterns

1. **Strategy Pattern (`ITokenizerStrategy` & Factory):**
* Decouple language-specific tokenizers (e.g., MeCab, Jieba, HanLP, or Unihan fallback) into pluggable parsing strategies.
* Use `ITokenizerStrategyFactory` to resolve parsing drivers dynamically at runtime based on `LanguageCode` and user preference.


2. **`Rune` & `ReadOnlySpan<char>` Zero-Allocation Processing:**
* Use `System.Text.Rune` for character iteration to handle CJK extension characters and surrogate pairs safely.
* Expose low-level alignment routines using `ReadOnlySpan<char>` and `ref struct` buffers to perform string manipulations without heap allocations.


3. **`IAsyncEnumerable` & `ReadOnlyMemory<char>` Async Pipelines:**
* Pass memory regions (`ReadOnlyMemory<char>`) across `async` boundaries to support non-blocking streaming.
* Stream tokens asynchronously (`IAsyncEnumerable<T>`) for large text inputs.


4. **Abstract Syntax Tree (AST) & Visitor Pattern (`IGlossAstVisitor`):**
* Standardize pipeline outputs into an immutable AST (`DocumentNode`, `RubyNode`, `TextNode`).
* Implement rendering logic using the Visitor pattern to decouple formatters (`HtmlRubyVisitor`, `AnkiSyntaxVisitor`, `JsonAstVisitor`).



---

### Architecture Contract & Interface Code

```csharp
namespace PhoneticGlossEngine.Core;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// --- Domain Models ---
public record LanguageCode(string Code); // e.g., "ja-JP", "zh-CN"

public record MemoryToken(
    ReadOnlyMemory<char> Text, 
    int StartIndex, 
    IReadOnlyList<string> ReadingCandidates, 
    string? PartOfSpeech
);

public record DisambiguatedToken(MemoryToken SourceToken, string ResolvedReading);

// --- 1. Zero-Alloc Span Models & Aligner ---
public readonly ref struct SpanGlossSegment
{
    public ReadOnlySpan<char> BaseText { get; }
    public ReadOnlySpan<char> GlossText { get; }
    public int StartIndex { get; }
    public int Length { get; }

    public SpanGlossSegment(ReadOnlySpan<char> baseText, ReadOnlySpan<char> glossText, int startIndex, int length)
    {
        BaseText = baseText; GlossText = glossText; StartIndex = startIndex; Length = length;
    }
}

public interface ISpanPhoneticAligner
{
    LanguageCode SupportedLanguage { get; }
    int AlignToSpan(ReadOnlySpan<char> baseText, ReadOnlySpan<char> fullReading, Span<SpanGlossSegment> destinationBuffer);
}

// --- 2. Strategy Pattern (Tokenizer & Disambiguator) ---
public interface ITokenizerStrategy
{
    LanguageCode SupportedLanguage { get; }
    string StrategyName { get; }
    IAsyncEnumerable<MemoryToken> TokenizeAsync(ReadOnlyMemory<char> text, CancellationToken cancellationToken = default);
}

public interface ITokenizerStrategyFactory
{
    ITokenizerStrategy GetStrategy(LanguageCode language, string? preferredStrategyName = null);
    void RegisterStrategy(ITokenizerStrategy strategy);
}

public interface IPhoneticDisambiguator
{
    LanguageCode SupportedLanguage { get; }
    IAsyncEnumerable<DisambiguatedToken> DisambiguateStreamAsync(
        IAsyncEnumerable<MemoryToken> tokens, CancellationToken cancellationToken = default);
}

// --- 3. AST Definitions ---
public abstract record GlossAstNode { public abstract void Accept(IGlossAstVisitor visitor); }
public record DocumentNode(IReadOnlyList<GlossAstNode> Children) : GlossAstNode { public override void Accept(IGlossAstVisitor visitor) => visitor.Visit(this); }
public record TextNode(string Value) : GlossAstNode { public override void Accept(IGlossAstVisitor visitor) => visitor.Visit(this); }
public record RubyNode(string BaseText, string GlossText) : GlossAstNode { public override void Accept(IGlossAstVisitor visitor) => visitor.Visit(this); }

// --- 4. Visitor Pattern ---
public interface IGlossAstVisitor
{
    void Visit(DocumentNode node);
    void Visit(TextNode node);
    void Visit(RubyNode node);
    string GetResult();
}

```

---

### End-to-End Execution Flow

```
Input Stream / Memory Buffer (ReadOnlyMemory<char>)
         │
         ▼
[ ITokenizerStrategyFactory ]
         │  (Selects strategy based on LanguageCode)
         ▼
[ ITokenizerStrategy.TokenizeAsync ]
         │  (Yields MemoryToken via IAsyncEnumerable, uses Rune for surrogate safety)
         ▼
[ IPhoneticDisambiguator.DisambiguateStreamAsync ]
         │  (Resolves polyphones / context readings)
         ▼
[ ISpanPhoneticAligner.AlignToSpan ]
         │  (Low-level zero-alloc matching via ReadOnlySpan<char>)
         ▼
[ DocumentNode (AST) ]
         │
         ▼
[ IGlossAstVisitor ]
         │  (Renders output: HTML <ruby>, Anki, LaTeX, or JSON)
         ▼
Target Formatted String

```

---

### Instructions for Implementation

1. **Unicode Safety:** Ensure all fallback character lookups iterate over `text.Span.EnumerateRunes()` rather than native `char` indices.
2. **Buffer Management:** Inside the pipeline's AST-assembly loop, use stack-allocated buffers (`stackalloc SpanGlossSegment[16]`) during span alignment to minimize allocations.
3. **Threading & Cancellation:** Propagate `CancellationToken` through all `IAsyncEnumerable` methods using the `[EnumeratorCancellation]` attribute.
4. **Concrete Deliverables:**
* Implement `HtmlRubyVisitor` (`<ruby>base<rt>gloss</rt></ruby>`) and `AnkiSyntaxVisitor` (`base[gloss]`).
* Provide a default `UnihanRuneStrategy` as a fallback tokenizer for standard CJK characters.