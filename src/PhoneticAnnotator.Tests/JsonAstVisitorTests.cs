using System.Text.Json;
using PhoneticAnnotator.Core;
using Unihan.Models;

namespace PhoneticAnnotator.Tests;

public sealed class JsonAstVisitorTests
{
    [Fact]
    public void Visit_Document_SerializesAllNodeTypesAndReadingMetadata()
    {
        var language = new LanguageCode("zh-HK");
        var readingSystem = ReadingSystem.CantoneseJyutping;
        var document = new DocumentNode(
        [
            new TextNode("plain"),
            new RubyNode("甲", "gaap3", language, readingSystem),
            new UnresolvedGlossNode("乙", ["jat1", "jyut6"], language, readingSystem),
            new PronunciationNode("عَرَبِيّ", "عَرَبِيّ", new LanguageCode("ar"), new ReadingSystem("ar-Arab", "Tashkeel", UnihanField.Unknown))
        ]);
        var visitor = new JsonAstVisitor();

        document.Accept(visitor);

        using var json = JsonDocument.Parse(visitor.GetResult());
        var root = json.RootElement;
        var children = root.GetProperty("children");

        Assert.Equal("document", root.GetProperty("type").GetString());
        Assert.Equal("text", children[0].GetProperty("type").GetString());
        Assert.Equal("plain", children[0].GetProperty("value").GetString());
        Assert.Equal("ruby", children[1].GetProperty("type").GetString());
        Assert.Equal("zh-HK", children[1].GetProperty("language").GetString());
        Assert.Equal("yue-Latn", children[1].GetProperty("readingSystem").GetProperty("tag").GetString());
        Assert.Equal("kCantonese", children[1].GetProperty("readingSystem").GetProperty("sourceField").GetString());
        Assert.Equal("unresolved", children[2].GetProperty("type").GetString());
        Assert.Equal(new[] { "jat1", "jyut6" }, children[2].GetProperty("readingCandidates")
            .EnumerateArray()
            .Select(candidate => candidate.GetString()));
        Assert.Equal("pronunciation", children[3].GetProperty("type").GetString());
        Assert.Equal("ar", children[3].GetProperty("language").GetString());
    }
}