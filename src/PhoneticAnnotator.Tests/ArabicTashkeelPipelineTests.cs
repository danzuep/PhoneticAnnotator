using System.Runtime.CompilerServices;
using System.Text.Json;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class ArabicTashkeelPipelineTests
{
    [Fact]
    public async Task FixtureProvider_ProducesPairedAnnotationAcrossRenderers()
    {
        var strategy = new FixtureArabicStrategy();
        var document = await GlossDocumentAssembler.AssembleAsync(
            strategy.TokenizeAsync("كتب".AsMemory()),
            new CandidateOnlyDisambiguator());

        var pronunciation = Assert.IsType<PronunciationNode>(Assert.Single(document.Children));
        Assert.Equal("كتب", pronunciation.BaseText);
        Assert.Equal("كَتَبَ", pronunciation.Pronunciation);

        var htmlVisitor = new HtmlRubyVisitor();
        document.Accept(htmlVisitor);
        Assert.Contains("<span class=\"phonetic-annotation\" lang=\"ar\">", htmlVisitor.GetResult());

        var ankiVisitor = new AnkiSyntaxVisitor();
        document.Accept(ankiVisitor);
        Assert.Equal("كتب[كَتَبَ]", ankiVisitor.GetResult());

        var jsonVisitor = new JsonAstVisitor();
        document.Accept(jsonVisitor);
        using var json = JsonDocument.Parse(jsonVisitor.GetResult());
        var node = json.RootElement.GetProperty("children")[0];
        Assert.Equal("pronunciation", node.GetProperty("type").GetString());
        Assert.Equal(
            "PairedPronunciation",
            node.GetProperty("readingSystem").GetProperty("annotationKind").GetString());
    }

    private sealed class FixtureArabicStrategy : ITokenizerStrategy
    {
        public LanguageCode SupportedLanguage { get; } = new("ar");

        public string StrategyName => "FixtureArabic";

        public async IAsyncEnumerable<MemoryToken> TokenizeAsync(
            ReadOnlyMemory<char> text,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new MemoryToken(
                text,
                0,
                SupportedLanguage,
                ReadingSystem.ArabicTashkeel,
                ["كَتَبَ"]);
        }
    }
}