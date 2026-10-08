using System.Text;
using Unihan.Models;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Identifies the language associated with a token or tokenizer.
/// </summary>
public sealed record LanguageCode
{
    /// <summary>
    /// Gets the BCP 47 language tag.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a language identifier.
    /// </summary>
    /// <param name="value">A non-empty language tag.</param>
    public LanguageCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }
}

/// <summary>
/// Identifies a reading system and the source property used to produce readings.
/// </summary>
/// <param name="Tag">The language and script tag for the reading.</param>
/// <param name="Name">A display name for the reading system.</param>
/// <param name="SourceField">The Unihan property supplying the reading.</param>
/// <param name="AnnotationKind">The rendering semantics for this reading.</param>
public sealed record ReadingSystem(
    string Tag,
    string Name,
    UnihanField SourceField,
    ReadingAnnotationKind AnnotationKind = ReadingAnnotationKind.Ruby)
{
    /// <summary>
    /// Gets the Mandarin Hanyu Pinyin reading system backed by <c>kMandarin</c>.
    /// </summary>
    public static ReadingSystem MandarinPinyin { get; } = new("cmn-Latn", "Hanyu Pinyin", UnihanField.kMandarin);

    /// <summary>
    /// Gets the Cantonese Jyutping reading system backed by <c>kCantonese</c>.
    /// </summary>
    public static ReadingSystem CantoneseJyutping { get; } = new("yue-Latn", "Jyutping", UnihanField.kCantonese);

    /// <summary>
    /// Gets the Japanese Kana reading system backed by <c>kJapanese</c>.
    /// </summary>
    public static ReadingSystem JapaneseKana { get; } = new("jpn-Kana", "Japanese Kana", UnihanField.kJapanese);

    /// <summary>
    /// Gets the Arabic Tashkeel annotation system.
    /// </summary>
    public static ReadingSystem ArabicTashkeel { get; } = new(
        "ar-Arab",
        "Tashkeel",
        UnihanField.Unknown,
        ReadingAnnotationKind.PairedPronunciation);
}

/// <summary>
/// Describes how a reading relates to its source text when rendered.
/// </summary>
public enum ReadingAnnotationKind
{
    /// <summary>Render the reading as ruby text associated with the source.</summary>
    Ruby,

    /// <summary>Render the reading as a paired pronunciation annotation.</summary>
    PairedPronunciation
}

/// <summary>
/// A source-backed token and its candidate readings.
/// </summary>
/// <param name="Text">A slice of the caller-owned input.</param>
/// <param name="StartIndex">The token's UTF-16 offset in the input.</param>
/// <param name="Language">The language being tokenized.</param>
/// <param name="ReadingSystem">The reading system selected for this token.</param>
/// <param name="ReadingCandidates">Zero or more candidate readings.</param>
public sealed record MemoryToken(
    ReadOnlyMemory<char> Text,
    int StartIndex,
    LanguageCode Language,
    ReadingSystem ReadingSystem,
    IReadOnlyList<string> ReadingCandidates);

/// <summary>
/// Looks up readings for an individual Unicode scalar value.
/// </summary>
public interface ICharacterReadingProvider
{
    /// <summary>
    /// Gets candidate readings for a scalar from a specific Unihan property.
    /// </summary>
    /// <param name="rune">The scalar value to look up.</param>
    /// <param name="field">The Unihan reading property to use.</param>
    /// <returns>Candidate readings, or an empty list when no reading is available.</returns>
    IReadOnlyList<string> GetReadings(Rune rune, UnihanField field);
}

/// <summary>
/// Tokenizes input into Unicode scalar-sized source slices using an Unihan field.
/// </summary>
public interface ITokenizerStrategy
{
    /// <summary>
    /// Gets the language handled by this strategy.
    /// </summary>
    LanguageCode SupportedLanguage { get; }

    /// <summary>
    /// Gets the strategy's stable identifier.
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// Produces source-backed tokens in input order.
    /// </summary>
    /// <param name="text">Input memory that must remain unchanged while tokens are consumed.</param>
    /// <param name="cancellationToken">A token used to cancel enumeration.</param>
    /// <returns>An asynchronous sequence of scalar-sized tokens.</returns>
    IAsyncEnumerable<MemoryToken> TokenizeAsync(
        ReadOnlyMemory<char> text,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Registers and resolves tokenizer strategies by language and optional preference.
/// </summary>
public interface ITokenizerStrategyFactory
{
    /// <summary>
    /// Gets the preferred registered strategy or the first registered strategy for a language.
    /// </summary>
    /// <param name="language">The input language.</param>
    /// <param name="preferredStrategyName">An optional strategy identifier to select.</param>
    /// <returns>The selected tokenizer strategy.</returns>
    /// <exception cref="KeyNotFoundException">No matching strategy is registered.</exception>
    ITokenizerStrategy GetStrategy(LanguageCode language, string? preferredStrategyName = null);

    /// <summary>
    /// Registers a strategy. Duplicate strategy identifiers for one language are rejected.
    /// </summary>
    /// <param name="strategy">The strategy to register.</param>
    /// <exception cref="InvalidOperationException">The language already has this strategy identifier.</exception>
    void RegisterStrategy(ITokenizerStrategy strategy);
}

/// <summary>
/// Describes whether a token's reading candidates were resolved.
/// </summary>
public enum ReadingResolution
{
    NoReading,
    Resolved,
    Ambiguous
}

/// <summary>
/// A token paired with its conservative reading-resolution result.
/// </summary>
/// <param name="SourceToken">The original token.</param>
/// <param name="Status">The resolution state.</param>
/// <param name="ResolvedReading">The reading when exactly one candidate is available.</param>
public sealed record DisambiguatedToken(
    MemoryToken SourceToken,
    ReadingResolution Status,
    string? ResolvedReading);

/// <summary>
/// Resolves reading candidates in an asynchronous token stream.
/// </summary>
public interface IPhoneticDisambiguator
{
    /// <summary>
    /// Resolves unambiguous candidate sets and preserves the remaining states.
    /// </summary>
    /// <param name="tokens">The input token sequence.</param>
    /// <param name="cancellationToken">A token used to cancel enumeration.</param>
    /// <returns>Tokens with explicit resolution status.</returns>
    IAsyncEnumerable<DisambiguatedToken> DisambiguateStreamAsync(
        IAsyncEnumerable<MemoryToken> tokens,
        CancellationToken cancellationToken = default);
}