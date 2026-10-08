using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class JapaneseOkuriganaAlignerTests
{
    [Fact]
    public void AlignToSpan_AmbiguousKanaMatchFallsBackToWholeToken()
    {
        var aligner = new JapaneseOkuriganaAligner();
        ReadOnlySpan<char> baseText = "食べる";
        ReadOnlySpan<char> reading = "たべるべる";
        Span<GlossSegmentOffsets> destination = stackalloc GlossSegmentOffsets[4];

        var count = aligner.AlignToSpan(baseText, reading, destination);

        Assert.Equal(1, count);
        Assert.Equal(0, destination[0].BaseStartIndex);
        Assert.Equal(baseText.Length, destination[0].BaseLength);
        Assert.Equal(0, destination[0].GlossStartIndex);
        Assert.Equal(reading.Length, destination[0].GlossLength);
        Assert.True(destination[0].IsAnnotated);
    }

    [Fact]
    public void AlignToSpan_WithoutKanaFallsBackToWholeToken()
    {
        var aligner = new JapaneseOkuriganaAligner();
        ReadOnlySpan<char> baseText = "東京";
        ReadOnlySpan<char> reading = "とうきょう";
        Span<GlossSegmentOffsets> destination = stackalloc GlossSegmentOffsets[2];

        var count = aligner.AlignToSpan(baseText, reading, destination);

        Assert.Equal(1, count);
        Assert.Equal(baseText.Length, destination[0].BaseLength);
        Assert.Equal(reading.Length, destination[0].GlossLength);
    }
}