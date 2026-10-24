namespace PhoneticAnnotator.Core;

/// <summary>Compatibility entry point for Japanese kana-run alignment.</summary>
public sealed class JapaneseOkuriganaAligner : ISpanPhoneticAligner
{
    private static readonly SpanCharacterAligner Aligner = new();

    public LanguageCode SupportedLanguage => Aligner.SupportedLanguage;

    public int AlignToSpan(
        ReadOnlySpan<char> baseText,
        ReadOnlySpan<char> fullReading,
        Span<GlossSegmentOffsets> destinationBuffer) =>
        Aligner.AlignToSpan(baseText, fullReading, destinationBuffer);
}
