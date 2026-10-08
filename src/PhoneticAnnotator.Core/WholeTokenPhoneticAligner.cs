namespace PhoneticAnnotator.Core;

/// <summary>
/// Conservatively pairs the whole source token with its whole reading.
/// </summary>
public sealed class WholeTokenPhoneticAligner(LanguageCode supportedLanguage) : ISpanPhoneticAligner
{
    /// <inheritdoc />
    public LanguageCode SupportedLanguage { get; } =
        supportedLanguage ?? throw new ArgumentNullException(nameof(supportedLanguage));

    /// <inheritdoc />
    public int AlignToSpan(
        ReadOnlySpan<char> baseText,
        ReadOnlySpan<char> fullReading,
        Span<GlossSegmentOffsets> destinationBuffer)
    {
        if (baseText.IsEmpty || fullReading.IsEmpty)
        {
            return 0;
        }

        if (destinationBuffer.IsEmpty)
        {
            throw new ArgumentException("The destination must hold one whole-token segment.", nameof(destinationBuffer));
        }

        destinationBuffer[0] = new GlossSegmentOffsets(0, baseText.Length, 0, fullReading.Length);
        return 1;
    }
}