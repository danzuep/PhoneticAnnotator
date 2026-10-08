using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Performs longest-match word tokenization against a caller-supplied pronunciation lexicon.
/// </summary>
public sealed class WordLexiconTokenizerStrategy : ITokenizerStrategy
{
    private static readonly IReadOnlyList<string> NoCandidates = Array.Empty<string>();

    private readonly WordReadingLexicon _lexicon;
    private readonly ReadingSystem _readingSystem;

    /// <summary>
    /// Creates a word tokenizer for one language and reading system.
    /// </summary>
    /// <param name="lexicon">The immutable word-to-reading trie.</param>
    /// <param name="supportedLanguage">The language of input entries.</param>
    /// <param name="readingSystem">The reading system associated with candidates.</param>
    public WordLexiconTokenizerStrategy(
        WordReadingLexicon lexicon,
        LanguageCode supportedLanguage,
        ReadingSystem readingSystem)
    {
        _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
        SupportedLanguage = supportedLanguage ?? throw new ArgumentNullException(nameof(supportedLanguage));
        _readingSystem = readingSystem ?? throw new ArgumentNullException(nameof(readingSystem));
        StrategyName = $"WordLexicon:{readingSystem.Tag}";
    }

    /// <inheritdoc />
    public LanguageCode SupportedLanguage { get; }

    /// <inheritdoc />
    public string StrategyName { get; }

    /// <inheritdoc />
    public IAsyncEnumerable<MemoryToken> TokenizeAsync(
        ReadOnlyMemory<char> text,
        CancellationToken cancellationToken = default) =>
        TokenizeCoreAsync(text, cancellationToken);

    private async IAsyncEnumerable<MemoryToken> TokenizeCoreAsync(
        ReadOnlyMemory<char> text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startIndex = 0;
        while (startIndex < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_lexicon.TryGetLongestMatch(
                SupportedLanguage,
                text.Span,
                startIndex,
                out var matchedLength,
                out var candidates))
            {
                yield return new MemoryToken(
                    text.Slice(startIndex, matchedLength),
                    startIndex,
                    SupportedLanguage,
                    _readingSystem,
                    candidates);
                startIndex += matchedLength;
                continue;
            }

            var status = Rune.DecodeFromUtf16(text.Span[startIndex..], out _, out var consumed);
            if (status != OperationStatus.Done)
            {
                consumed = 1;
            }

            yield return new MemoryToken(
                text.Slice(startIndex, consumed),
                startIndex,
                SupportedLanguage,
                _readingSystem,
                NoCandidates);
            startIndex += consumed;
        }
    }
}