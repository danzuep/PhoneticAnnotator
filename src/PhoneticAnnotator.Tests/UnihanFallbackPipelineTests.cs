using System.Text;
using System.Text.Json;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class UnihanFallbackPipelineTests
{
    [Fact]
    public async Task LoadedUnihanData_FlowsThroughLanguageSelectionAndRendering()
    {
        const string data = "U+7532\tkMandarin\tjiǎ\nU+7532\tkCantonese\tgaap3\nU+7532\tkJapanese\tこう\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var lookup = await UnihanLookupLoader.LoadAsync(stream);
        var factory = new TokenizerStrategyFactory();
        UnihanStrategyRegistration.RegisterFallbacks(
            factory,
            new UnihanCharacterReadingProvider(lookup));

        var cases = new[]
        {
            (new LanguageCode("zh-CN"), "cmn-Latn", "jiǎ"),
            (new LanguageCode("zh-HK"), "yue-Latn", "gaap3"),
            (new LanguageCode("ja-JP"), "jpn-Kana", "こう")
        };

        foreach (var (language, readingSystemTag, expectedReading) in cases)
        {
            var strategy = factory.GetStrategy(language);
            var document = await GlossDocumentAssembler.AssembleAsync(
                strategy.TokenizeAsync("甲".AsMemory()),
                new CandidateOnlyDisambiguator());
            var visitor = new JsonAstVisitor();
            document.Accept(visitor);

            using var json = JsonDocument.Parse(visitor.GetResult());
            var ruby = json.RootElement.GetProperty("children")[0];
            Assert.Equal("ruby", ruby.GetProperty("type").GetString());
            Assert.Equal(expectedReading, ruby.GetProperty("glossText").GetString());
            Assert.Equal(readingSystemTag, ruby.GetProperty("readingSystem").GetProperty("tag").GetString());
        }
    }
}