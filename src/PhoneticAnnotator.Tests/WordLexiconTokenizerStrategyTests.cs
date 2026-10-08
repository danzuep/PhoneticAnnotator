using System.Text;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class WordLexiconTokenizerStrategyTests
{
    [Fact]
    public async Task TokenizeAsync_UsesLongestWordMatchAndPreservesUnknownText()
    {
        const string data = "# language<TAB>word<TAB>reading candidates\nzh-CN\t你好\tni hao\nzh-CN\t你好啊\tni hao a\nzh-CN\t重庆\tchong qing|zhong qing\nzh-HK\t重庆\tcung4 hing3\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var lexicon = await WordReadingLexicon.LoadAsync(stream);
        var strategy = CreateStrategy(lexicon, new LanguageCode("zh-CN"), ReadingSystem.MandarinPinyin);

        var tokens = await CollectAsync(strategy, "你好啊重庆!".AsMemory());

        Assert.True(stream.CanRead);
        Assert.Collection(
            tokens,
            token =>
            {
                Assert.Equal("你好啊", token.Text.ToString());
                Assert.Equal(0, token.StartIndex);
                Assert.Equal(new[] { "ni hao a" }, token.ReadingCandidates);
            },
            token =>
            {
                Assert.Equal("重庆", token.Text.ToString());
                Assert.Equal(3, token.StartIndex);
                Assert.Equal(new[] { "chong qing", "zhong qing" }, token.ReadingCandidates);
            },
            token =>
            {
                Assert.Equal("!", token.Text.ToString());
                Assert.Equal(5, token.StartIndex);
                Assert.Empty(token.ReadingCandidates);
            });

        var document = await GlossDocumentAssembler.AssembleAsync(
            strategy.TokenizeAsync("你好啊重庆!".AsMemory()),
            new CandidateOnlyDisambiguator());
        Assert.IsType<RubyNode>(document.Children[0]);
        Assert.IsType<UnresolvedGlossNode>(document.Children[1]);
        Assert.IsType<TextNode>(document.Children[2]);

        var cantoneseStrategy = CreateStrategy(lexicon, new LanguageCode("zh-HK"), ReadingSystem.CantoneseJyutping);
        var cantoneseToken = Assert.Single(await CollectAsync(cantoneseStrategy, "重庆".AsMemory()));
        Assert.Equal(new[] { "cung4 hing3" }, cantoneseToken.ReadingCandidates);
    }

    [Fact]
    public async Task TokenizeAsync_ProducesPairedArabicAnnotationFromSuppliedLexicon()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ar\tكتب\tكَتَبَ\n"));
        var lexicon = await WordReadingLexicon.LoadAsync(stream);
        var strategy = CreateStrategy(lexicon, new LanguageCode("ar"), ReadingSystem.ArabicTashkeel);

        var document = await GlossDocumentAssembler.AssembleAsync(
            strategy.TokenizeAsync("كتب".AsMemory()),
            new CandidateOnlyDisambiguator());

        var pronunciation = Assert.IsType<PronunciationNode>(Assert.Single(document.Children));
        Assert.Equal("كَتَبَ", pronunciation.Pronunciation);
    }

    [Fact]
    public async Task LoadAsync_InvalidRowsFailWithLineNumber()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("word\n"));

        var exception = await Assert.ThrowsAsync<FormatException>(() => WordReadingLexicon.LoadAsync(stream));

        Assert.Contains("line 1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WordLexiconTokenizerStrategy CreateStrategy(
        WordReadingLexicon lexicon,
        LanguageCode language,
        ReadingSystem readingSystem) =>
        new(lexicon, language, readingSystem);

    private static async Task<List<MemoryToken>> CollectAsync(
        ITokenizerStrategy strategy,
        ReadOnlyMemory<char> text)
    {
        var tokens = new List<MemoryToken>();
        await foreach (var token in strategy.TokenizeAsync(text))
        {
            tokens.Add(token);
        }

        return tokens;
    }
}