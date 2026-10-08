using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Unihan.Models;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Provides a per-scalar fallback tokenizer backed by an indexed Unihan lookup.
/// </summary>
public sealed class UnihanRuneStrategy : ITokenizerStrategy
{
    private static readonly IReadOnlyList<string> NoReadings = Array.Empty<string>();

    private readonly ICharacterReadingProvider _readingProvider;
    private readonly UnihanField _field;
    private readonly ReadingSystem _readingSystem;

    /// <summary>
    /// Creates a tokenizer that uses one Unihan field for every Unicode scalar.
    /// </summary>
    /// <param name="readingProvider">The indexed character-reading provider.</param>
    /// <param name="supportedLanguage">The language of the input text.</param>
    /// <param name="readingSystem">The reading system associated with the selected field.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The reading system does not match the selected field.</exception>
    public UnihanRuneStrategy(
        ICharacterReadingProvider readingProvider,
        LanguageCode supportedLanguage,
        ReadingSystem readingSystem)
    {
        _readingProvider = readingProvider ?? throw new ArgumentNullException(nameof(readingProvider));
        SupportedLanguage = supportedLanguage ?? throw new ArgumentNullException(nameof(supportedLanguage));
        _readingSystem = readingSystem ?? throw new ArgumentNullException(nameof(readingSystem));
        _field = readingSystem.SourceField;

        if (_field == UnihanField.Unknown)
        {
            throw new ArgumentException("A concrete Unihan reading field is required.", nameof(readingSystem));
        }
    }

    /// <inheritdoc />
    public LanguageCode SupportedLanguage { get; }

    /// <inheritdoc />
    public string StrategyName => "UnihanRune";

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

            var status = Rune.DecodeFromUtf16(text.Span[startIndex..], out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                yield return new MemoryToken(
                    text.Slice(startIndex, 1),
                    startIndex,
                    SupportedLanguage,
                    _readingSystem,
                    NoReadings);
                startIndex++;
                continue;
            }

            var candidates = _readingProvider.GetReadings(rune, _field);
            yield return new MemoryToken(
                text.Slice(startIndex, consumed),
                startIndex,
                SupportedLanguage,
                _readingSystem,
                candidates);
            startIndex += consumed;
        }
    }
}