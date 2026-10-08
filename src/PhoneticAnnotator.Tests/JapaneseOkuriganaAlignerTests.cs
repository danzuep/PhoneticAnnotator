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

    [Fact]
    public void AlignToSpan_MixedScriptOkuriganaMatchesAcrossHiraganaAndKatakana()
    {
        var aligner = new JapaneseOkuriganaAligner();
        ReadOnlySpan<char> baseText = "日本語かな";
        ReadOnlySpan<char> reading = "ニホンゴカナ";
        Span<GlossSegmentOffsets> destination = stackalloc GlossSegmentOffsets[5];

        var count = aligner.AlignToSpan(baseText, reading, destination);

        Assert.Equal(2, count);
        Assert.Equal(0, destination[0].BaseStartIndex);
        Assert.Equal(3, destination[0].BaseLength);
        Assert.Equal(0, destination[0].GlossStartIndex);
        Assert.Equal(4, destination[0].GlossLength);
        Assert.True(destination[0].IsAnnotated);
        Assert.Equal(3, destination[1].BaseStartIndex);
        Assert.Equal(2, destination[1].BaseLength);
        Assert.Equal(4, destination[1].GlossStartIndex);
        Assert.Equal(2, destination[1].GlossLength);
        Assert.False(destination[1].IsAnnotated);
    }

    [Fact]
    public void AlignToSpan_AlignsMultipleOkuriganaRunsInOrder()
    {
        var aligner = new JapaneseOkuriganaAligner();
        ReadOnlySpan<char> baseText = "書き直す";
        ReadOnlySpan<char> reading = "かきなおす";
        Span<GlossSegmentOffsets> destination = stackalloc GlossSegmentOffsets[4];

        var count = aligner.AlignToSpan(baseText, reading, destination);

        Assert.Equal(4, count);
        Assert.Equal((0, 1, 0, 1, true), ToTuple(destination[0]));
        Assert.Equal((1, 1, 1, 1, false), ToTuple(destination[1]));
        Assert.Equal((2, 1, 2, 2, true), ToTuple(destination[2]));
        Assert.Equal((3, 1, 4, 1, false), ToTuple(destination[3]));
    }

    private static (int BaseStart, int BaseLength, int GlossStart, int GlossLength, bool IsAnnotated) ToTuple(
        GlossSegmentOffsets segment) =>
        (
            segment.BaseStartIndex,
            segment.BaseLength,
            segment.GlossStartIndex,
            segment.GlossLength,
            segment.IsAnnotated);
}