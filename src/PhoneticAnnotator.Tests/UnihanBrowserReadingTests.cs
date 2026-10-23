using System.Text;
using System.Text.Json;
using PhoneticAnnotator.Core;
using PhoneticAnnotator.Wasm;

namespace PhoneticAnnotator.Tests;

public sealed class UnihanBrowserReadingTests
{
    private const string Data = """
        # Selected Unicode reading formats
        U+7684	kMandarin	de
        U+7684	kCantonese	dik1
        U+7684	kJapanese	テキ チャク まと
        U+7684	kJapaneseOn	TEKI
        U+7684	kJapaneseKun	MATO AKIRAKA
        U+7684	kHangul	적:0E
        U+7684	kKorean	CEK
        U+7684	kVietnamese	đích
        U+7684	kZhuang	dik
        U+7684	kHanyuPinlu	de(75596) dì(157) dí(84)
        U+7684	kHanyuPinyin	42644.160:dì,dí,de
        U+7684	kTGHZ2013	069.080:de 070.170:dī
        U+7684	kXHC1983	0224.040:de 0231.060:dí
        U+7684	kSMSZD2003Readings	dì粵dik1 dí粵dik1 de粵dik1
        U+20000	kMandarin	hē
        """;

    [Theory]
    [InlineData("kMandarin", "de")]
    [InlineData("kCantonese", "dik1")]
    [InlineData("kJapanese", "テキ")]
    [InlineData("kJapaneseOn", "TEKI")]
    [InlineData("kJapaneseKun", "MATO")]
    [InlineData("kHangul", "적")]
    [InlineData("kKorean", "CEK")]
    [InlineData("kVietnamese", "đích")]
    [InlineData("kZhuang", "dik")]
    [InlineData("kHanyuPinlu", "de")]
    [InlineData("kHanyuPinyin", "dì")]
    [InlineData("kTGHZ2013", "de")]
    [InlineData("kXHC1983", "de")]
    [InlineData("kSMSZD2003Readings", "dì")]
    [InlineData("kSMSZD2003Readings-Cantonese", "dik1")]
    public async Task ModernReadingSystems_NormalizeMetadataAndSelectFirst(string id, string expected)
    {
        var session = new AnnotationSession();
        await session.LoadUnihanDictionary(Encoding.UTF8.GetBytes(Data), id);
        using var document = JsonDocument.Parse(await session.AnnotateToJson("的", Options(id, true)));
        var node = document.RootElement.GetProperty("children")[0];
        Assert.Equal("ruby", node.GetProperty("type").GetString());
        Assert.Equal(expected, node.GetProperty("glossText").GetString());
    }

    [Fact]
    public async Task CandidateSelection_IsOptionalAndPreservesSourceOrder()
    {
        var session = new AnnotationSession();
        await session.LoadUnihanDictionary(Encoding.UTF8.GetBytes(Data), "kHanyuPinyin");
        using var document = JsonDocument.Parse(await session.AnnotateToJson("的", Options("kHanyuPinyin", false)));
        var node = document.RootElement.GetProperty("children")[0];
        Assert.Equal("unresolved", node.GetProperty("type").GetString());
        Assert.Equal(new[] { "dì", "dí", "de" }, node.GetProperty("readingCandidates")
            .EnumerateArray().Select(candidate => candidate.GetString()));
    }

    [Fact]
    public async Task FirstReading_AlsoWorksForWordStrategy()
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t重庆\tchóng qìng|zhòng qìng\n"));
        using var document = JsonDocument.Parse(await session.AnnotateToJson(
            "重庆", """{"language":"zh-CN","pickFirstReading":true}"""));
        Assert.Equal("chóng qìng", document.RootElement.GetProperty("children")[0].GetProperty("glossText").GetString());
    }

    [Fact]
    public async Task FailedUnihanLoad_RetainsPublishedSnapshotAndSupplementaryCharacters()
    {
        var session = new AnnotationSession();
        await session.LoadUnihanDictionary(Encoding.UTF8.GetBytes(Data), "kMandarin");
        await Assert.ThrowsAsync<FormatException>(() => session.LoadUnihanDictionary(
            Encoding.UTF8.GetBytes("U+7684\tkMandarin\tbad\ninvalid"), "kMandarin"));
        await Assert.ThrowsAsync<FormatException>(() => session.LoadUnihanDictionary(
            Encoding.UTF8.GetBytes("U+D800\tkMandarin\tbad"), "kMandarin"));
        using var document = JsonDocument.Parse(await session.AnnotateToJson("𠀀的😀", Options("kMandarin", true)));
        var nodes = document.RootElement.GetProperty("children");
        Assert.Equal("hē", nodes[0].GetProperty("glossText").GetString());
        Assert.Equal("的", nodes[1].GetProperty("baseText").GetString());
        Assert.Equal("😀", nodes[2].GetProperty("value").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.AnnotateToHtml("的", Options("kHangul", true)));
    }

    [Fact]
    public async Task WordStrategy_RejectsAlternateFieldsAndPreservesArabicLanguageTags()
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("ar-Arab\tكتب\tكَتَبَ\n"));
        using var document = JsonDocument.Parse(await session.AnnotateToJson("كتب", """{"language":"ar-Arab"}"""));
        Assert.Equal("كَتَبَ", document.RootElement.GetProperty("children")[0].GetProperty("pronunciation").GetString());
        await Assert.ThrowsAsync<ArgumentException>(() => session.AnnotateToHtml(
            "食べる", """{"language":"ja-JP","readingSystem":"kJapaneseOn","strategy":"word"}"""));
    }

    [Fact]
    public async Task UnihanReadingHtml_IsEncoded()
    {
        var session = new AnnotationSession();
        await session.LoadUnihanDictionary(Encoding.UTF8.GetBytes("U+7684\tkMandarin\t<img>"), "kMandarin");
        var html = await session.AnnotateToHtml("<script>的", Options("kMandarin", true));
        Assert.DoesNotContain("<img>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("<ruby>", html);
    }

    [Fact]
    public async Task EveryCatalogEntry_LoadsBundledUnicodeData()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../data/Unihan_Readings.txt"));
        foreach (var option in UnihanReadingOption.All)
        {
            using var stream = File.OpenRead(path);
            var dictionary = await UnihanReadingDictionary.LoadAsync(stream, option.Id);
            Assert.Equal(option, dictionary.Option);
        }
    }

    private static string Options(string id, bool first) =>
        $$"""{"readingSystem":"{{id}}","strategy":"unihan","pickFirstReading":{{first.ToString().ToLowerInvariant()}}}""";
}
