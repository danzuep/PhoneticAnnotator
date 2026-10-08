namespace PhoneticAnnotator.Core;

/// <summary>
/// Stackalloc-friendly offsets for a source segment and its corresponding reading.
/// </summary>
public readonly record struct GlossSegmentOffsets(
    int BaseStartIndex,
    int BaseLength,
    int GlossStartIndex,
    int GlossLength);

/// <summary>
/// Aligns source text and a complete reading into caller-provided span segments.
/// </summary>
public interface ISpanPhoneticAligner
{
    /// <summary>
    /// Gets the language handled by this aligner.
    /// </summary>
    LanguageCode SupportedLanguage { get; }

    /// <summary>
    /// Writes aligned offsets without allocating managed result objects.
    /// Slice the input spans with the returned offsets to access each segment.
    /// </summary>
    /// <param name="baseText">The source text.</param>
    /// <param name="fullReading">The complete reading for the source text.</param>
    /// <param name="destinationBuffer">Caller-provided storage for alignment offsets.</param>
    /// <returns>The number of segments written, or zero if either input is empty.</returns>
    /// <exception cref="ArgumentException">The destination cannot hold the required segment.</exception>
    int AlignToSpan(
        ReadOnlySpan<char> baseText,
        ReadOnlySpan<char> fullReading,
        Span<GlossSegmentOffsets> destinationBuffer);
}