using System.Text;
using PhoneticAnnotator.Core;
using Unihan.Models;

namespace PhoneticAnnotator.Tests;

public sealed class UnihanRuneStrategyTests
{
    [Fact]
    public async Task AssembleAsync_ResolvesSingleCandidateAndPreservesAmbiguity()
    {
        var lookup = new UnihanLookup();
        lookup.AddEntry(0x7532, UnihanField.kMandarin, "jiǎ");
        lookup.AddEntry(0x4E50, UnihanField.kMandarin, "lè yuè");
        var strategy = CreateStrategy(lookup);

        var document = await GlossDocumentAssembler.AssembleAsync(
            strategy.TokenizeAsync("甲乐A".AsMemory()),
            new CandidateOnlyDisambiguator());

        Assert.Collection(
            document.Children,
            node => Assert.Equal("jiǎ", Assert.IsType<RubyNode>(node).GlossText),
            node => Assert.Equal(new[] { "lè", "yuè" }, Assert.IsType<UnresolvedGlossNode>(node).ReadingCandidates),
            node => Assert.Equal("A", Assert.IsType<TextNode>(node).Value));
    }

    [Fact]
    public void HtmlRubyVisitor_EncodesBaseAndReading()
    {
        var node = new RubyNode(
            "<甲>",
            "<jiǎ>",
            new LanguageCode("zh-CN"),
            ReadingSystem.MandarinPinyin);
        var visitor = new HtmlRubyVisitor();

        node.Accept(visitor);

        Assert.Equal("<ruby>&lt;&#x7532;&gt;<rt>&lt;ji&#x1CE;&gt;</rt></ruby>", visitor.GetResult());
    }

    [Fact]
    public void AnkiSyntaxVisitor_EscapesSyntaxDelimiters()
    {
        var node = new RubyNode(
            "甲[乙]",
            "jiǎ\\yuè",
            new LanguageCode("zh-CN"),
            ReadingSystem.MandarinPinyin);
        var visitor = new AnkiSyntaxVisitor();

        node.Accept(visitor);

        Assert.Equal("甲\\[乙\\][jiǎ\\\\yuè]", visitor.GetResult());
    }

    [Fact]
    public async Task TokenizeAsync_PreservesSupplementaryRuneSlicesAndUtf16Offsets()
    {
        var source = "𠀀甲A";
        var lookup = new UnihanLookup();
        lookup.AddEntry(0x20000, UnihanField.kMandarin, "qiū qiú");
        lookup.AddEntry(0x7532, UnihanField.kMandarin, "jiǎ");
        var strategy = CreateStrategy(lookup);

        var tokens = await CollectTokensAsync(strategy, source.AsMemory());

        Assert.Equal(3, tokens.Count);
        Assert.Equal("𠀀", tokens[0].Text.ToString());
        Assert.Equal(0, tokens[0].StartIndex);
        Assert.Equal(new[] { "qiū", "qiú" }, tokens[0].ReadingCandidates);
        Assert.Equal("甲", tokens[1].Text.ToString());
        Assert.Equal(2, tokens[1].StartIndex);
        Assert.Equal(new[] { "jiǎ" }, tokens[1].ReadingCandidates);
        Assert.Equal("A", tokens[2].Text.ToString());
        Assert.Equal(3, tokens[2].StartIndex);
        Assert.Empty(tokens[2].ReadingCandidates);
        Assert.Equal("cmn-Latn", tokens[0].ReadingSystem.Tag);
    }

    [Fact]
    public async Task TokenizeAsync_PreservesUnpairedSurrogateAsUnannotatedSource()
    {
        var source = new string(new[] { '\uD800', 'A' });
        var strategy = CreateStrategy(new UnihanLookup());

        var tokens = await CollectTokensAsync(strategy, source.AsMemory());

        Assert.Equal(2, tokens.Count);
        Assert.Equal(source.AsMemory(0, 1), tokens[0].Text);
        Assert.Empty(tokens[0].ReadingCandidates);
        Assert.Equal("A", tokens[1].Text.ToString());
        Assert.Equal(1, tokens[1].StartIndex);
    }

    [Fact]
    public async Task TokenizeAsync_ThrowsWhenEnumerationIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var strategy = CreateStrategy(new UnihanLookup());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in strategy.TokenizeAsync("甲".AsMemory(), cancellation.Token))
            {
            }
        });
    }

    [Fact]
    public void UnihanCharacterReadingProvider_UsesRequestedField()
    {
        var lookup = new UnihanLookup();
        lookup.AddEntry(0x7532, UnihanField.kMandarin, "jiǎ");
        lookup.AddEntry(0x7532, UnihanField.kCantonese, "gaap3");
        var provider = new UnihanCharacterReadingProvider(lookup);

        var mandarin = provider.GetReadings(new Rune(0x7532), UnihanField.kMandarin);
        var cantonese = provider.GetReadings(new Rune(0x7532), UnihanField.kCantonese);

        Assert.Equal(new[] { "jiǎ" }, mandarin);
        Assert.Equal(new[] { "gaap3" }, cantonese);
    }

    private static UnihanRuneStrategy CreateStrategy(UnihanLookup lookup) =>
        new(
            new UnihanCharacterReadingProvider(lookup),
            new LanguageCode("zh-CN"),
            ReadingSystem.MandarinPinyin);

    private static async Task<List<MemoryToken>> CollectTokensAsync(
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
