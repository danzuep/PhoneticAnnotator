using System.Net;
using System.Text;
using System.Text.Json;
using PhoneticAnnotator.Wasm;

namespace PhoneticAnnotator.Tests;

public sealed class WasmAnnotationSessionTests
{
    [Fact]
    public async Task EmptyDictionary_PreservesUnicodeAndEncodesHtml()
    {
        var session = new AnnotationSession();
        const string text = "未知𠀀😀<script>&\"";

        var html = await session.AnnotateToHtml(text, "{}");
        using var json = JsonDocument.Parse(await session.AnnotateToJson(text, "{}"));

        Assert.Equal(text, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script>", html);
        Assert.Equal("document", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(text, string.Concat(json.RootElement.GetProperty("children")
            .EnumerateArray().Select(node => node.GetProperty("value").GetString())));
    }

    [Fact]
    public async Task Chunks_AreCumulativeAndUseLongestMatchThenNewestTie()
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t你\tnǐ\nzh-CN\t你好\tnǐ hǎo\n"));
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t你\tnew\nja-JP\t食べる\tたべる\n"));

        using var json = JsonDocument.Parse(await session.AnnotateToJson("你好你", """{"language":"ZH-cn"}"""));
        var children = json.RootElement.GetProperty("children");

        Assert.Equal(2, children.GetArrayLength());
        Assert.Equal("nǐ hǎo", children[0].GetProperty("glossText").GetString());
        Assert.Equal("new", children[1].GetProperty("glossText").GetString());
        Assert.Contains("<ruby>", await session.AnnotateToHtml("食べる", "{}"));
    }

    [Fact]
    public async Task MalformedChunk_IsAtomicAndDoesNotPoisonFutureLoads()
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t你\tnǐ\n"));

        await Assert.ThrowsAsync<FormatException>(() =>
            session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t好\thǎo\nbad line\n")));
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t们\tmen\n"));

        using var json = JsonDocument.Parse(await session.AnnotateToJson("你好们", """{"language":"zh-CN"}"""));
        var children = json.RootElement.GetProperty("children");
        Assert.Equal("ruby", children[0].GetProperty("type").GetString());
        Assert.Equal("text", children[1].GetProperty("type").GetString());
        Assert.Equal("ruby", children[2].GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task JapaneseAlignment_RespectsOption(bool align, int expectedNodes)
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("ja-JP\t食べる\tたべる\n"));

        using var json = JsonDocument.Parse(await session.AnnotateToJson(
            "食べる", $$"""{"alignOkurigana":{{align.ToString().ToLowerInvariant()}}}"""));

        Assert.Equal(expectedNodes, json.RootElement.GetProperty("children").GetArrayLength());
    }

    [Fact]
    public async Task AmbiguousReadings_ArePreservedInJsonButNotGuessedInHtml()
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t重庆\tchóng qìng|zhòng qìng\n"));

        using var json = JsonDocument.Parse(await session.AnnotateToJson("重庆", """{"language":"zh-CN"}"""));
        var node = json.RootElement.GetProperty("children")[0];

        Assert.Equal("unresolved", node.GetProperty("type").GetString());
        Assert.Equal(2, node.GetProperty("readingCandidates").GetArrayLength());
        Assert.DoesNotContain("<ruby>", await session.AnnotateToHtml("重庆", """{"language":"zh-CN"}"""));
    }

    [Fact]
    public async Task Html_EncodesDictionaryReadingsAndSourceText()
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t<script>\t<img src=x onerror=alert(1)>\n"));

        var html = await session.AnnotateToHtml("<script>", """{"language":"zh-CN"}""");

        Assert.Contains("<ruby>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public async Task ConcurrentLoadsAndAnnotations_PublishCompleteSnapshots()
    {
        var session = new AnnotationSession();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = Enumerable.Range(0, 64).Select(index => Task.Run(async () =>
        {
            await start.Task;
            await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes($"zh-CN\t词{index}\treading{index}\n"));
            using var json = JsonDocument.Parse(await session.AnnotateToJson($"词{index}", """{"language":"zh-CN"}"""));
            Assert.Equal("ruby", json.RootElement.GetProperty("children")[0].GetProperty("type").GetString());
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(work);

        for (var index = 0; index < 64; index++)
        {
            using var json = JsonDocument.Parse(await session.AnnotateToJson($"词{index}", """{"language":"zh-CN"}"""));
            Assert.Equal($"reading{index}", json.RootElement.GetProperty("children")[0].GetProperty("glossText").GetString());
        }
    }

    [Fact]
    public async Task ConcurrentOverrides_KeepEachAnnotationOnOneSnapshot()
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t词\tinitial\n"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            await start.Task;
            await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes($"zh-CN\t词\treading{index}\n"));
        })).ToArray();
        var reader = Task.Run(async () =>
        {
            await start.Task;
            for (var index = 0; index < 32; index++)
            {
                using var json = JsonDocument.Parse(await session.AnnotateToJson(
                    new string('词', 64), """{"language":"zh-CN"}"""));
                var readings = json.RootElement.GetProperty("children").EnumerateArray()
                    .Select(node => node.GetProperty("glossText").GetString()).Distinct();
                Assert.Single(readings);
            }
        });
        start.SetResult();
        await Task.WhenAll(writers.Append(reader));

        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("zh-CN\t词\tlast\n"));
        using var final = JsonDocument.Parse(await session.AnnotateToJson("词", """{"language":"zh-CN"}"""));
        Assert.Equal("last", final.RootElement.GetProperty("children")[0].GetProperty("glossText").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"language":"unsupported"}""")]
    [InlineData("""{"language":null}""")]
    public async Task InvalidOptions_RejectWithoutChangingDictionary(string options)
    {
        var session = new AnnotationSession();

        await Assert.ThrowsAsync<ArgumentException>(() => session.AnnotateToHtml("text", options));
        Assert.Equal(string.Empty, await session.AnnotateToHtml(string.Empty, "{}"));
    }

    [Fact]
    public async Task MalformedJsonAndEmptyChunk_Reject()
    {
        var session = new AnnotationSession();

        await Assert.ThrowsAnyAsync<JsonException>(() => session.AnnotateToJson("text", "{"));
        await Assert.ThrowsAsync<ArgumentException>(() => session.LoadDictionaryChunk([]));
    }
}
