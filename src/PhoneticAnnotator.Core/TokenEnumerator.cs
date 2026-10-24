using System.Buffers;
using System.Text;

namespace PhoneticAnnotator.Core;

/// <summary>A borrowed token. Text and candidates remain owned by the input and dictionaries.</summary>
public readonly ref struct SpanToken(
    ReadOnlySpan<char> text,
    int startIndex,
    LanguageCode language,
    ReadingSystem readingSystem,
    IReadOnlyList<string> readingCandidates)
{
    public ReadOnlySpan<char> Text { get; } = text;
    public int StartIndex { get; } = startIndex;
    public LanguageCode Language { get; } = language;
    public ReadingSystem ReadingSystem { get; } = readingSystem;
    public IReadOnlyList<string> ReadingCandidates { get; } = readingCandidates;
}

/// <summary>
/// Allocation-free longest-match enumeration over borrowed input and immutable dictionary snapshots.
/// Equal-length matches prefer the newest (last) lexicon. A supplied Unihan dictionary selects
/// scalar-only tokenization instead of word matching, matching the session's Unihan strategy.
/// </summary>
public ref struct TokenEnumerator
{
    private readonly ReadOnlySpan<char> _text;
    private readonly ReadOnlySpan<WordReadingLexicon> _lexicons;
    private readonly LanguageCode _language;
    private readonly ReadingSystem _readingSystem;
    private readonly UnihanReadingDictionary? _unihan;
    private readonly CancellationToken _cancellationToken;
    private int _offset;
    private int _plainEnd;

    public TokenEnumerator(
        ReadOnlySpan<char> text,
        ReadOnlySpan<WordReadingLexicon> lexicons,
        LanguageCode language,
        ReadingSystem readingSystem,
        UnihanReadingDictionary? unihan = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(readingSystem);
        _text = text;
        _lexicons = lexicons;
        _language = language;
        _readingSystem = readingSystem;
        _unihan = unihan;
        _cancellationToken = cancellationToken;
        _offset = 0;
        _plainEnd = 0;
        Current = default;
    }

    public SpanToken Current { get; private set; }

    public TokenEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_offset >= _text.Length)
        {
            Current = default;
            return false;
        }

        var length = 0;
        IReadOnlyList<string> candidates = Array.Empty<string>();
        if (_unihan is null)
        {
            foreach (var lexicon in _lexicons)
            {
                if (lexicon.TryGetLongestMatch(_language, _text, _offset, out var matchedLength, out var readings)
                    && matchedLength >= length)
                {
                    length = matchedLength;
                    candidates = readings;
                }
            }
        }

        if (length == 0)
        {
            if (_offset >= _plainEnd)
            {
                _plainEnd = _offset + ScriptBoundaries.IndexOfBoundary(_text[_offset..]);
            }

            // SIMD-scanned ordinary BMP runs need no scalar decoding. Dictionary probes still
            // happen at every offset: script transitions must never change longest-match semantics.
            if (_unihan is null && _offset < _plainEnd)
            {
                length = 1;
            }
            else if (Rune.DecodeFromUtf16(_text[_offset..], out var rune, out length) != OperationStatus.Done)
            {
                length = 1;
            }
            else if (_unihan is not null)
            {
                candidates = _unihan.GetReadings(rune);
            }
        }

        Current = new SpanToken(_text.Slice(_offset, length), _offset, _language, _readingSystem, candidates);
        _offset += length;
        return true;
    }
}

internal static class ScriptBoundaries
{
#if NET8_0_OR_GREATER
    private static readonly SearchValues<char> Boundaries = SearchValues.Create(CreateBoundaryCharacters());

    private static string CreateBoundaryCharacters()
    {
        var characters = new StringBuilder();
        for (var value = 0; value <= char.MaxValue; value++)
        {
            if (IsBoundary((char)value)) characters.Append((char)value);
        }
        return characters.ToString();
    }
#endif

    public static int IndexOfBoundary(ReadOnlySpan<char> text)
    {
#if NET8_0_OR_GREATER
        var index = text.IndexOfAny(Boundaries);
        return index < 0 ? text.Length : index;
#else
        // The portable NetWasm BCL does not expose SearchValues.
        for (var index = 0; index < text.Length; index++)
        {
            if (IsBoundary(text[index])) return index;
        }
        return text.Length;
#endif
    }

    private static bool IsBoundary(char value) =>
        value is >= '\u2E80' and <= '\u9FFF' // CJK, hiragana, katakana and CJK punctuation
            or >= '\uF900' and <= '\uFAFF'
            or >= '\uFF00' and <= '\uFFEF' // Halfwidth kana and punctuation
            or >= '\u2000' and <= '\u206F'
            or >= '\uD800' and <= '\uDFFF' // Decode supplementary and malformed UTF-16 individually
            or >= '\u0021' and <= '\u002F'
            or >= '\u003A' and <= '\u0040'
            or >= '\u005B' and <= '\u0060'
            or >= '\u007B' and <= '\u007E';
}
