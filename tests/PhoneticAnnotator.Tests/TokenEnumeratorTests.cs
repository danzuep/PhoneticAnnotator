using System.Text;
using PhoneticAnnotator.Core;
using PhoneticAnnotator.Wasm;

namespace PhoneticAnnotator.Tests;

public sealed class TokenEnumeratorTests
{
    private static readonly LanguageCode Japanese = new("ja-JP");

    [Fact]
    public async Task SynchronousTokensMatchAsyncIncludingMalformedUtf16AndCrossScriptWords()
    {
        var lexicon = await Load("ja-JP\t食べる\tたべる\nja-JP\tA。カ漢\tえー\nja-JP\t𠀀あ\tかな\n");
        var strategy = new WordLexiconTokenizerStrategy(lexicon, Japanese, ReadingSystem.JapaneseKana);
        const string text = "abcA。カ漢!食べる𠀀あ😀\uD800z\uDC00ヰゑ。";
        var expected = new List<MemoryToken>();
        await foreach (var token in strategy.TokenizeAsync(text.AsMemory())) expected.Add(token);

        var enumerator = strategy.Tokenize(text);
        foreach (var token in expected)
        {
            Assert.True(enumerator.MoveNext());
            Assert.Equal(token.Text.ToString(), enumerator.Current.Text.ToString());
            Assert.Equal(token.StartIndex, enumerator.Current.StartIndex);
            Assert.Same(token.ReadingCandidates, enumerator.Current.ReadingCandidates);
            Assert.Same(token.Language, enumerator.Current.Language);
            Assert.Same(token.ReadingSystem, enumerator.Current.ReadingSystem);
        }
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public async Task LongestMatchWinsAndNewestChunkBreaksTies()
    {
        var old = await Load("ja-JP\t東京\tとうきょう\nja-JP\t東京都\tとうきょうと\n");
        var newest = await Load("ja-JP\t東京\tトウキョウ\n");
        var tokens = new TokenEnumerator("東京都東京", new[] { old, newest }, Japanese, ReadingSystem.JapaneseKana);
        Assert.True(tokens.MoveNext());
        Assert.Equal("東京都", tokens.Current.Text.ToString());
        Assert.Equal("とうきょうと", Assert.Single(tokens.Current.ReadingCandidates));
        Assert.True(tokens.MoveNext());
        Assert.Equal(3, tokens.Current.StartIndex);
        Assert.Equal("トウキョウ", Assert.Single(tokens.Current.ReadingCandidates));
        Assert.False(tokens.MoveNext());
    }

    [Fact]
    public void MissingDictionaryPreservesEveryScalarAndMalformedCodeUnit()
    {
        const string text = "A漢あア。𠀀😀\uD800x\uDC00";
        var expected = new[] { "A", "漢", "あ", "ア", "。", "𠀀", "😀", "\uD800", "x", "\uDC00" };
        var tokens = new TokenEnumerator(text, Array.Empty<WordReadingLexicon>(), Japanese, ReadingSystem.JapaneseKana);
        var offset = 0;
        foreach (var item in expected)
        {
            Assert.True(tokens.MoveNext());
            Assert.Equal(item, tokens.Current.Text.ToString());
            Assert.Equal(offset, tokens.Current.StartIndex);
            Assert.Empty(tokens.Current.ReadingCandidates);
            offset += item.Length;
        }
        Assert.False(tokens.MoveNext());
        Assert.Empty(GlossDocumentAssembler.Assemble(new TokenEnumerator(
            "", Array.Empty<WordReadingLexicon>(), Japanese, ReadingSystem.JapaneseKana)).Children);
    }

    [Fact]
    public async Task UnihanUsesScalarModeAndBorrowsCandidates()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "U+6F22\tkJapanese\tカン\nU+20000\tkJapanese\tかな\n"));
        var dictionary = await UnihanReadingDictionary.LoadAsync(stream, "kJapanese");
        var lexicon = await Load("ja-JP\t漢𠀀\tword\n");
        var tokens = new TokenEnumerator("漢𠀀\uD800", new[] { lexicon }, Japanese, ReadingSystem.JapaneseKana, dictionary);
        Assert.True(tokens.MoveNext());
        Assert.Equal("漢", tokens.Current.Text.ToString());
        Assert.Same(dictionary.GetReadings(new Rune('漢')), tokens.Current.ReadingCandidates);
        Assert.True(tokens.MoveNext());
        Assert.Equal("𠀀", tokens.Current.Text.ToString());
        Assert.Equal(1, tokens.Current.StartIndex);
        Assert.Equal("かな", Assert.Single(tokens.Current.ReadingCandidates));
        Assert.True(tokens.MoveNext());
        Assert.Empty(tokens.Current.ReadingCandidates);
        Assert.False(tokens.MoveNext());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SynchronousAssemblyMatchesAsync(bool pickFirst, bool align)
    {
        var lexicon = await Load("ja-JP\t食べる\tたべる\nja-JP\t生\tせい|なま\nja-JP\t今日\tきょう\n");
        var strategy = new WordLexiconTokenizerStrategy(lexicon, Japanese, ReadingSystem.JapaneseKana);
        ISpanPhoneticAligner? aligner = align ? new SpanCharacterAligner() : null;
        const string text = "食べる生今日!𠀀";
        var expected = await GlossDocumentAssembler.AssembleAsync(
            strategy.TokenizeAsync(text.AsMemory()), new CandidateOnlyDisambiguator(pickFirst), aligner);
        var actual = GlossDocumentAssembler.Assemble(strategy.Tokenize(text), pickFirst, aligner);
        Assert.Equal(Render(expected), Render(actual));
    }

    [Fact]
    public async Task ArabicAssemblyMatchesAsync()
    {
        var lexicon = await Load("ar\tكتب\tكَتَبَ\n");
        var strategy = new WordLexiconTokenizerStrategy(lexicon, new("ar"), ReadingSystem.ArabicTashkeel);
        var expected = await GlossDocumentAssembler.AssembleAsync(
            strategy.TokenizeAsync("كتب!".AsMemory()), new CandidateOnlyDisambiguator());
        Assert.Equal(Render(expected), Render(GlossDocumentAssembler.Assemble(strategy.Tokenize("كتب!"))));
    }

    [Fact]
    public async Task SteadyStateEnumerationAllocatesNothing()
    {
        var lexicon = await Load("ja-JP\t食べる\tたべる\nja-JP\t生\tせい|なま\n");
        WordReadingLexicon[] lexicons = [lexicon];
        const string text = "ordinary text 食べる生漢あア。𠀀😀\uD800end";
        for (var iteration = 0; iteration < 100; iteration++) Consume(text, lexicons);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        for (var iteration = 0; iteration < 100; iteration++) count += Consume(text, lexicons);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(count > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task SessionWithoutDictionaryReturnsPlainTextAndMissingUnihanFailsAsTask()
    {
        var session = new AnnotationSession();
        var html = await session.AnnotateToHtml("漢𠀀", "{}");
        Assert.DoesNotContain("<ruby", html);
        Assert.Equal("漢𠀀", System.Net.WebUtility.HtmlDecode(html));
        var task = session.AnnotateToHtml("漢", "{\"strategy\":\"unihan\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    [Theory]
    [InlineData("th", true)]
    [InlineData("th-TH", true)]
    [InlineData("th", false)]
    [InlineData("th-TH", false)]
    public async Task SessionSupportsCallerSuppliedThaiWords(string language, bool explicitReading)
    {
        var session = new AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes(
            $"{language}\tภาษา\tphaa-saa\n{language}\tภาษาไทย\tphaa-saa thai\n"));
        var options = explicitReading
            ? $"{{\"language\":\"{language}\",\"readingSystem\":\"th\",\"strategy\":\"word\"}}"
            : $"{{\"language\":\"{language}\"}}";

        var actual = await session.AnnotateToJson("ภาษาไทย!", options);
        var lexicon = await Load($"{language}\tภาษาไทย\tphaa-saa thai\n");
        var expected = GlossDocumentAssembler.Assemble(new TokenEnumerator(
            "ภาษาไทย!", new[] { lexicon }, new LanguageCode(language),
            new ReadingSystem("th", "Thai", Unihan.Models.UnihanField.Unknown)));

        Assert.Equal(Render(expected), actual);
        var ruby = Assert.IsType<RubyNode>(expected.Children[0]);
        Assert.Equal("ภาษาไทย", ruby.BaseText);
        Assert.IsType<TextNode>(expected.Children[1]);
    }

    [Fact]
    public async Task ThaiRequiresMatchingLanguageAndDoesNotInventSegmentation()
    {
        var session = new AnnotationSession();
        var html = await session.AnnotateToHtml("ภาษาไทย", "{\"language\":\"th\"}");
        Assert.DoesNotContain("<ruby", html);
        Assert.Equal("ภาษาไทย", System.Net.WebUtility.HtmlDecode(html));
        await Assert.ThrowsAsync<ArgumentException>(() => session.AnnotateToJson(
            "ภาษาไทย", "{\"language\":\"ja-JP\",\"readingSystem\":\"th\"}"));
    }

    [Fact]
    public void CancellationIsObservedByEnumerationAndAssembly()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ConsumeCancelled(cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => GlossDocumentAssembler.Assemble(
            new TokenEnumerator("", Array.Empty<WordReadingLexicon>(), Japanese, ReadingSystem.JapaneseKana),
            cancellationToken: cancellation.Token));
    }

    private static void ConsumeCancelled(CancellationToken token)
    {
        var tokens = new TokenEnumerator("漢", Array.Empty<WordReadingLexicon>(), Japanese,
            ReadingSystem.JapaneseKana, cancellationToken: token);
        tokens.MoveNext();
    }

    private static int Consume(string text, WordReadingLexicon[] lexicons)
    {
        var count = 0;
        foreach (var token in new TokenEnumerator(text, lexicons, Japanese, ReadingSystem.JapaneseKana))
            count += token.Text.Length + token.ReadingCandidates.Count;
        return count;
    }

    private static string Render(DocumentNode document)
    {
        var visitor = new JsonAstVisitor();
        document.Accept(visitor);
        return visitor.GetResult();
    }

    private static async Task<WordReadingLexicon> Load(string data)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        return await WordReadingLexicon.LoadAsync(stream);
    }
}
