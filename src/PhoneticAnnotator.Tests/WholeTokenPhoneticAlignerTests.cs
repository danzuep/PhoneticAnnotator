using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class WholeTokenPhoneticAlignerTests
{
    [Fact]
    public void AlignToSpan_WritesOneBorrowedSegment()
    {
        var aligner = new WholeTokenPhoneticAligner(new LanguageCode("zh-CN"));
        ReadOnlySpan<char> baseText = "𠀀甲";
        ReadOnlySpan<char> reading = "qiū jiǎ";
        Span<GlossSegmentOffsets> destination = stackalloc GlossSegmentOffsets[1];

        var count = aligner.AlignToSpan(baseText, reading, destination);

        Assert.Equal(1, count);
        Assert.Equal("𠀀甲", baseText.Slice(destination[0].BaseStartIndex, destination[0].BaseLength).ToString());
        Assert.Equal("qiū jiǎ", reading.Slice(destination[0].GlossStartIndex, destination[0].GlossLength).ToString());
        Assert.Equal(0, destination[0].BaseStartIndex);
        Assert.Equal(3, destination[0].BaseLength);
        Assert.True(destination[0].IsAnnotated);
    }

    [Fact]
    public void AlignToSpan_EmptyInputReturnsNoSegments()
    {
        var aligner = new WholeTokenPhoneticAligner(new LanguageCode("zh-CN"));

        var count = aligner.AlignToSpan("甲", ReadOnlySpan<char>.Empty, Span<GlossSegmentOffsets>.Empty);

        Assert.Equal(0, count);
    }

    [Fact]
    public void AlignToSpan_InsufficientDestinationThrowsWithoutWriting()
    {
        var aligner = new WholeTokenPhoneticAligner(new LanguageCode("zh-CN"));
        var threw = false;

        try
        {
            aligner.AlignToSpan("甲", "jiǎ", Span<GlossSegmentOffsets>.Empty);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Assert.True(threw);
    }
}