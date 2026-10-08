namespace PhoneticAnnotator.Core;

/// <summary>
/// Stackalloc-friendly offsets for a source segment and its corresponding reading.
/// </summary>
/// <param name="BaseStartIndex">The segment's start in the source text, in UTF-16 code units.</param>
/// <param name="BaseLength">The source segment's length in UTF-16 code units.</param>
/// <param name="GlossStartIndex">The segment's start in the complete reading, in UTF-16 code units.</param>
/// <param name="GlossLength">The reading segment's length in UTF-16 code units.</param>
/// <param name="IsAnnotated">Whether the segment should be rendered with a reading annotation.</param>
public readonly record struct GlossSegmentOffsets(
    int BaseStartIndex,
    int BaseLength,
    int GlossStartIndex,
    int GlossLength,
    bool IsAnnotated = true);

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
    /// The segments must partition the complete source and reading in order; unannotated segments still consume their reading offsets.
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