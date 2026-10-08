using System.Runtime.CompilerServices;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class GlossDocumentAssemblerTests
{
    [Fact]
    public async Task AssembleAsync_UsesAlignedSegmentsInOrder()
    {
        var language = new LanguageCode("zh-CN");
        var token = new MemoryToken(
            "甲乙".AsMemory(),
            0,
            language,
            ReadingSystem.MandarinPinyin,
            ["jia yi"]);

        var document = await GlossDocumentAssembler.AssembleAsync(
            OneTokenAsync(token),
            new CandidateOnlyDisambiguator(),
            new SplittingAligner(language));

        Assert.Collection(
            document.Children,
            node =>
            {
                var ruby = Assert.IsType<RubyNode>(node);
                Assert.Equal("甲", ruby.BaseText);
                Assert.Equal("jia ", ruby.GlossText);
            },
            node =>
            {
                var ruby = Assert.IsType<RubyNode>(node);
                Assert.Equal("乙", ruby.BaseText);
                Assert.Equal("yi", ruby.GlossText);
            });
    }

    private static async IAsyncEnumerable<MemoryToken> OneTokenAsync(
        MemoryToken token,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return token;
    }

    private sealed class SplittingAligner(LanguageCode supportedLanguage) : ISpanPhoneticAligner
    {
        public LanguageCode SupportedLanguage { get; } = supportedLanguage;

        public int AlignToSpan(
            ReadOnlySpan<char> baseText,
            ReadOnlySpan<char> fullReading,
            Span<GlossSegmentOffsets> destinationBuffer)
        {
            if (destinationBuffer.Length < 2)
            {
                throw new ArgumentException("Two segments are required.", nameof(destinationBuffer));
            }

            destinationBuffer[0] = new GlossSegmentOffsets(0, 1, 0, 4);
            destinationBuffer[1] = new GlossSegmentOffsets(1, 1, 4, 2);
            return 2;
        }
    }
}