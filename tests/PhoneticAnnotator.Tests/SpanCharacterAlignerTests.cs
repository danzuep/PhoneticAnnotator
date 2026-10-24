using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class SpanCharacterAlignerTests
{
    [Theory]
    [InlineData("食べる", "タベル", 1, 1)]
    [InlineData("書き直す", "カキナオス", 1, 1)]
    [InlineData("問ヰる", "トヰル", 1, 1)]
    [InlineData("問ゑる", "トヱル", 1, 1)]
    [InlineData("𠀀べる", "タベル", 2, 1)]
    [InlineData("問𛀁る", "ト𛀁る", 1, 1)]
    public void AlignsKanaWithoutChangingUtf16Offsets(
        string text, string reading, int firstBaseLength, int firstReadingLength)
    {
        Span<GlossSegmentOffsets> segments = stackalloc GlossSegmentOffsets[text.Length];
        var count = new SpanCharacterAligner().AlignToSpan(text, reading, segments);

        Assert.True(count > 1);
        Assert.Equal(firstBaseLength, segments[0].BaseLength);
        Assert.Equal(firstReadingLength, segments[0].GlossLength);
        Assert.True(segments[0].IsAnnotated);
        Assert.Equal(firstBaseLength, segments[1].BaseStartIndex);
        Assert.Equal(firstReadingLength, segments[1].GlossStartIndex);
        Assert.False(segments[1].IsAnnotated);
        AssertPartition(text, reading, segments[..count]);
    }

    [Theory]
    [InlineData("今日", "きょう")]
    [InlineData("大人", "おとな")]
    [InlineData("食べる", "たべるべる")]
    [InlineData("行ゐる", "いわる")]
    [InlineData("食べる", "たべる余")]
    public void JukujikunAndUncertainAnchorsStayWhole(string text, string reading)
    {
        Span<GlossSegmentOffsets> segments = stackalloc GlossSegmentOffsets[text.Length];
        var count = new SpanCharacterAligner().AlignToSpan(text, reading, segments);

        Assert.Equal(1, count);
        Assert.Equal(text.Length, segments[0].BaseLength);
        Assert.Equal(reading.Length, segments[0].GlossLength);
        Assert.True(segments[0].IsAnnotated);
    }

    [Fact]
    public void LongReadingsUseReusableScratchAndPreserveOutput()
    {
        var text = new string('漢', 300) + "べる";
        var reading = new string('カ', 400) + "ベル";
        var aligner = new SpanCharacterAligner();
        var segments = new GlossSegmentOffsets[4];
        Assert.Equal(2, aligner.AlignToSpan(text, reading, segments));

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            if (aligner.AlignToSpan(text, reading, segments) != 2)
                throw new InvalidOperationException();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(300, segments[0].BaseLength);
        Assert.Equal(400, segments[0].GlossLength);
        AssertPartition(text, reading, segments.AsSpan(0, 2));
    }

    [Fact]
    public void EmptyAndSmallDestinationsKeepCompatibility()
    {
        var aligner = new SpanCharacterAligner();
        Assert.Equal(0, aligner.AlignToSpan("", "かな", Span<GlossSegmentOffsets>.Empty));
        Assert.Equal(0, aligner.AlignToSpan("漢", "", Span<GlossSegmentOffsets>.Empty));
        Assert.Throws<ArgumentException>(() => aligner.AlignToSpan("漢", "かん", Span<GlossSegmentOffsets>.Empty));
        Span<GlossSegmentOffsets> one = stackalloc GlossSegmentOffsets[1];
        Assert.Equal(1, aligner.AlignToSpan("食べる", "たべる", one));
        Assert.Equal(3, one[0].BaseLength);
    }

    [Theory]
    [InlineData("食べる", "タベル")]
    [InlineData("書き直す", "かきなおす")]
    [InlineData("ゐる", "ヰル")]
    [InlineData("𛀁る", "𛀁る")]
    [InlineData("\uD800べる", "\uDC00ベル")]
    public void CompatibilityEntryReturnsIdenticalSegments(string text, string reading)
    {
        var current = new GlossSegmentOffsets[text.Length];
        var compatibility = new GlossSegmentOffsets[text.Length];
        var count = new SpanCharacterAligner().AlignToSpan(text, reading, current);
        var compatibilityCount = new JapaneseOkuriganaAligner().AlignToSpan(text, reading, compatibility);
        Assert.Equal(compatibilityCount, count);
        Assert.Equal(compatibility, current);
    }

    private static void AssertPartition(string text, string reading, ReadOnlySpan<GlossSegmentOffsets> segments)
    {
        var baseOffset = 0;
        var readingOffset = 0;
        foreach (var segment in segments)
        {
            Assert.Equal(baseOffset, segment.BaseStartIndex);
            Assert.Equal(readingOffset, segment.GlossStartIndex);
            baseOffset += segment.BaseLength;
            readingOffset += segment.GlossLength;
        }
        Assert.Equal(text.Length, baseOffset);
        Assert.Equal(reading.Length, readingOffset);
    }
}
