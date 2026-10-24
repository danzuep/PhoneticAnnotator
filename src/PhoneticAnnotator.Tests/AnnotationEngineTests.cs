using System.Text;
using PhoneticAnnotator.Core;
using Unihan.Models;

namespace PhoneticAnnotator.Tests;

public sealed class AnnotationEngineTests
{
    [Fact]
    public async Task FluentAndNestedActions_ProduceEquivalentDocuments()
    {
        var lexicon = await LoadWords("ja-JP\t食べる\tたべる\n");
        var fluent = new AnnotationEngineBuilder()
            .Language("ja-JP").Reading(ReadingSystem.JapaneseKana)
            .Dictionaries(d => d.Words(lexicon))
            .Filters(f => f.Script(JapaneseScript.Katakana))
            .Build();
        var actions = AnnotationEngine.Create(b => b
            .Reading("kJapanese")
            .Dictionaries(d => d.Words(lexicon))
            .Filters(f => f.Script(JapaneseScript.Katakana)));

        var document = fluent.Annotate("食べる");
        Assert.Equal(fluent.ToJson(document), actions.ToJson(actions.Annotate("食べる")));
        Assert.Equal("タ", Assert.IsType<RubyNode>(document.Children[0]).GlossText);
        Assert.Equal("べる", Assert.IsType<TextNode>(document.Children[1]).Value);
    }

    [Fact]
    public async Task Build_SnapshotsBuilderAndNestedBuilderCollections()
    {
        var original = await LoadWords("ja-JP\t猫\tねこ\n");
        var replacement = await LoadWords("ja-JP\t猫\tにゃんこ\n");
        AnnotationDictionaryBuilder? retainedDictionaries = null;
        GlossFilterPipelineBuilder? retainedFilters = null;
        var sourceArray = new[] { original };
        var builder = new AnnotationEngineBuilder().Reading("kJapanese")
            .Dictionaries(d =>
            {
                retainedDictionaries = d;
                d.Words(sourceArray);
            })
            .Filters(f => retainedFilters = f);
        var first = builder.Build();
        sourceArray[0] = replacement;
        retainedDictionaries!.Words(replacement);
        retainedFilters!.Script(JapaneseScript.Katakana);
        builder.PickFirstReading().WithoutAlignment();
        var second = builder.Build();

        Assert.Equal("猫[ねこ]", first.ToAnki(first.Annotate("猫")));
        Assert.False(first.PickFirstReading);
        Assert.Equal("猫[ニャンコ]", second.ToAnki(second.Annotate("猫")));
        Assert.True(second.PickFirstReading);
    }

    [Fact]
    public async Task BuiltInKnowledgeHelpers_CopyMutableInput()
    {
        var words = new HashSet<string> { "猫" };
        var kanji = new HashSet<Rune> { new('犬') };
        var levels = new Dictionary<Rune, int> { [new('鳥')] = 5 };
        var lexicon = await LoadWords("ja-JP\t猫\tねこ\nja-JP\t犬\tいぬ\nja-JP\t鳥\tとり\n");
        var engine = AnnotationEngine.Create(b => b.Reading("kJapanese")
            .Dictionaries(d => d.Words(lexicon))
            .Filters(f => f.KnownWords(words).KnownKanji(kanji).Jlpt(5, levels)));
        words.Clear();
        kanji.Clear();
        levels.Clear();

        Assert.All(engine.Annotate("猫犬鳥").Children, node => Assert.IsType<TextNode>(node));
    }

    [Fact]
    public async Task Candidates_DefaultToUnresolved_AndCanPickFirst()
    {
        var lexicon = await LoadWords("zh-CN\t重\tchóng|zhòng\n");
        var builder = new AnnotationEngineBuilder().Reading("kMandarin")
            .Dictionaries(d => d.Words(lexicon));
        var unresolved = builder.Build();
        var first = builder.PickFirstReading().Build();

        Assert.Equal(2, Assert.IsType<UnresolvedGlossNode>(Assert.Single(unresolved.Annotate("重").Children))
            .ReadingCandidates.Count);
        Assert.Equal("chóng", Assert.IsType<RubyNode>(Assert.Single(first.Annotate("重").Children)).GlossText);
    }

    [Fact]
    public async Task Dictionaries_UseLongestMatchThenNewestTie()
    {
        var first = await LoadWords("zh-CN\t你\told\nzh-CN\t你好\tnǐ hǎo\n");
        var second = await LoadWords("zh-CN\t你\tnew\n");
        var engine = AnnotationEngine.Create(b => b.Reading("kMandarin")
            .Dictionaries(d => d.Words(first).Words(second)));

        Assert.Equal("你好[nǐ hǎo]你[new]", engine.ToAnki(engine.Annotate("你好你")));
    }

    [Fact]
    public async Task Unihan_AnnotatesScalars_AndLeavesStreamOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("U+4F60\tkMandarin\tnǐ\n"));
        var dictionary = await UnihanReadingDictionary.LoadAsync(stream, "kMandarin");
        var engine = AnnotationEngine.Create(b => b.Reading("kMandarin")
            .Dictionaries(d => d.Unihan(dictionary)));

        Assert.True(stream.CanRead);
        Assert.Equal("你[nǐ]好", engine.ToAnki(engine.Annotate("你好")));
    }

    [Fact]
    public async Task Rendering_IsIndependentAndCreatesNewVisitors()
    {
        var lexicon = await LoadWords("zh-CN\t你\tnǐ\n");
        var engine = AnnotationEngine.Create(b => b.Reading("kMandarin").Dictionaries(d => d.Words(lexicon)));
        var document = engine.Annotate("你");
        var html = engine.ToHtml(document);
        var json = engine.ToJson(document);
        var anki = engine.ToAnki(document);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(html, engine.ToHtml(document));
            Assert.Equal(json, engine.ToJson(document));
            Assert.Equal(anki, engine.ToAnki(document));
        }
        var calls = 0;
        IGlossAstVisitor? previous = null;
        string Render() => engine.Render(document, () =>
        {
            calls++;
            var visitor = new CountingVisitor();
            Assert.NotSame(previous, visitor);
            previous = visitor;
            return visitor;
        });
        Assert.Equal("1", Render());
        Assert.Equal("1", Render());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CustomFilters_RunInRegistrationOrder_AndNormalizeRoot()
    {
        var order = new List<int>();
        var engine = AnnotationEngine.Create(b => b.Reading("kJapanese")
            .Filters(f => f.Add(new CallbackFilter(node =>
            {
                Assert.IsType<DocumentNode>(node);
                order.Add(1);
                return new TextNode("replacement");
            })))
            .Filters(f => f.Add(new CallbackFilter(node =>
            {
                order.Add(2);
                Assert.Equal("replacement", Assert.IsType<TextNode>(Assert.Single(
                    Assert.IsType<DocumentNode>(node).Children)).Value);
                return node;
            }))));

        Assert.Equal("replacement", engine.ToHtml(engine.Annotate("猫")));
        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public void StandaloneFilterBuilder_BuildsIndependentPipelines()
    {
        var source = new RubyNode("猫", "ねこ", new("ja-JP"), ReadingSystem.JapaneseKana);
        var builder = new GlossFilterPipelineBuilder().Script(JapaneseScript.Katakana);
        var first = builder.Build();
        var second = builder.KnownWords(new[] { "猫" }).Build();

        Assert.Equal("ネコ", Assert.IsType<RubyNode>(first.Apply(source)).GlossText);
        Assert.IsType<TextNode>(second.Apply(source));
    }

    [Fact]
    public void NullFilterResult_IsRejected()
    {
        var engine = AnnotationEngine.Create(b => b.Reading("kJapanese")
            .Filters(f => f.Add(new CallbackFilter(_ => null!))));

        Assert.Throws<InvalidOperationException>(() => engine.Annotate(""));
    }

    [Fact]
    public void Cancellation_IsObservedForEmptyInputAndBetweenFilters()
    {
        var empty = AnnotationEngine.Create(b => b.Reading("kJapanese"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => empty.Annotate("", cancelled.Token));

        using var during = new CancellationTokenSource();
        var reachedSecond = false;
        var engine = AnnotationEngine.Create(b => b.Reading("kJapanese").Filters(f => f
            .Add(new CallbackFilter(node => { during.Cancel(); return node; }))
            .Add(new CallbackFilter(node => { reachedSecond = true; return node; }))));
        Assert.Throws<OperationCanceledException>(() => engine.Annotate("猫", during.Token));
        Assert.False(reachedSecond);
    }

    [Fact]
    public async Task Cancellation_IsObservedDuringAlignment()
    {
        var lexicon = await LoadWords("ja-JP\t猫\tねこ\n");
        using var cancellation = new CancellationTokenSource();
        var engine = AnnotationEngine.Create(b => b.Reading("kJapanese")
            .Dictionaries(d => d.Words(lexicon)).Alignment(new CancellingAligner(cancellation)));

        Assert.Throws<OperationCanceledException>(() => engine.Annotate("猫猫", cancellation.Token));
    }

    [Fact]
    public async Task WithoutAlignment_PreservesWholeToken()
    {
        var lexicon = await LoadWords("ja-JP\t食べる\tたべる\n");
        var engine = AnnotationEngine.Create(b => b.Reading("kJapanese")
            .WithoutAlignment().Dictionaries(d => d.Words(lexicon)));

        Assert.Equal("食べる[たべる]", engine.ToAnki(engine.Annotate("食べる")));
    }

    [Fact]
    public async Task CustomDisplayNames_PreserveReadingCompatibilityAndAutomaticAlignment()
    {
        var reading = ReadingSystem.JapaneseKana with { Name = "My furigana", Tag = "JPN-kana" };
        var words = await LoadWords("ja-JP\t食べる\tたべる\n");
        var engine = AnnotationEngine.Create(b => b.Language("ja-JP").Reading(reading)
            .Dictionaries(d => d.Words(words)));
        var document = engine.Annotate("食べる");

        Assert.Equal(2, document.Children.Count);
        Assert.Equal(reading, Assert.IsType<RubyNode>(document.Children[0]).ReadingSystem);
        Assert.Equal("食[た]べる", engine.ToAnki(document));

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("U+732B\tkJapanese\tねこ\n"));
        var dictionary = await UnihanReadingDictionary.LoadAsync(stream, "kJapanese");
        var scalarEngine = AnnotationEngine.Create(b => b.Language("ja-JP").Reading(reading)
            .Dictionaries(d => d.Unihan(dictionary)));
        Assert.Equal("猫[ねこ]", scalarEngine.ToAnki(scalarEngine.Annotate("猫")));
    }

    [Fact]
    public async Task ArabicReading_ProducesPronunciation()
    {
        var lexicon = await LoadWords("ar\tكتاب\tكِتَاب\n");
        var engine = AnnotationEngine.Create(b => b.Language("ar").Reading(ReadingSystem.ArabicTashkeel)
            .Dictionaries(d => d.Words(lexicon)));

        Assert.IsType<PronunciationNode>(Assert.Single(engine.Annotate("كتاب").Children));
    }

    [Fact]
    public void RequiredAndUnsupportedConfigurations_AreRejectedAtBuild()
    {
        Assert.Throws<InvalidOperationException>(() => new AnnotationEngineBuilder().Build());
        Assert.Throws<InvalidOperationException>(() => new AnnotationEngineBuilder().Language("ja-JP").Build());
        Assert.Throws<InvalidOperationException>(() => new AnnotationEngineBuilder().Reading(ReadingSystem.JapaneseKana).Build());
        Assert.Throws<ArgumentException>(() => new AnnotationEngineBuilder().Language("zh-CN")
            .Reading(ReadingSystem.JapaneseKana).Build());
        Assert.Throws<ArgumentException>(() => new AnnotationEngineBuilder().Language("ja-JP")
            .Reading(new ReadingSystem("jpn-Kana", "Invalid", UnihanField.kDefinition)).Build());
        Assert.Throws<ArgumentException>(() => new AnnotationEngineBuilder().Reading("kMandarin")
            .Alignment(new SpanCharacterAligner()).Build());
        Assert.Throws<ArgumentException>(() => new AnnotationEngineBuilder().Reading("kJapaneseOn")
            .Alignment(new SpanCharacterAligner()).Build());
        Assert.Throws<ArgumentException>(() => new AnnotationEngineBuilder().Language("ar")
            .Reading(ReadingSystem.ArabicTashkeel)
            .Alignment(new WholeTokenPhoneticAligner(new("ar"))).Build());
        Assert.Throws<ArgumentException>(() => new AnnotationEngineBuilder().Language("ja-JP")
            .Reading(ReadingSystem.ArabicTashkeel).Build());
        Assert.Throws<ArgumentException>(() => new AnnotationEngineBuilder().Reading("not-a-reading"));
        Assert.Throws<ArgumentException>(() => new AnnotationEngineBuilder().Language("th")
            .Reading(new ReadingSystem("th", "Thai", UnihanField.Unknown, (ReadingAnnotationKind)999)).Build());
    }

    [Fact]
    public async Task UnihanDictionaryMismatchAndMixedStrategies_AreRejected()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("U+4F60\tkMandarin\tnǐ\n"));
        var dictionary = await UnihanReadingDictionary.LoadAsync(stream, "kMandarin");
        var words = await LoadWords("zh-CN\t你\tnǐ\n");
        Assert.Throws<ArgumentException>(() => AnnotationEngine.Create(b => b.Reading("kJapanese")
            .Dictionaries(d => d.Unihan(dictionary))));
        Assert.Throws<ArgumentException>(() => AnnotationEngine.Create(b => b.Reading("kHanyuPinyin")
            .Dictionaries(d => d.Unihan(dictionary))));
        Assert.Throws<InvalidOperationException>(() => AnnotationEngine.Create(b => b.Reading("kMandarin")
            .Dictionaries(d => d.Words(words).Unihan(dictionary))));
    }

    [Fact]
    public void NullConfigurationAndRendering_AreRejected()
    {
        var builder = new AnnotationEngineBuilder();
        Assert.Throws<ArgumentNullException>(() => AnnotationEngine.Create(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Language((LanguageCode)null!));
        Assert.Throws<ArgumentException>(() => builder.Language(""));
        Assert.Throws<ArgumentNullException>(() => builder.Reading((ReadingSystem)null!));
        Assert.Throws<ArgumentNullException>(() => builder.Alignment(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Dictionaries(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Filters(null!));
        Assert.Throws<ArgumentNullException>(() => new AnnotationDictionaryBuilder().Words(null!));
        Assert.Throws<ArgumentException>(() => new AnnotationDictionaryBuilder().Words(new WordReadingLexicon[] { null! }));
        Assert.Throws<ArgumentNullException>(() => new AnnotationDictionaryBuilder().Unihan(null!));
        Assert.Throws<ArgumentNullException>(() => new GlossFilterPipelineBuilder().Add(null!));
        var engine = builder.Reading("kJapanese").Build();
        Assert.Throws<ArgumentNullException>(() => engine.Render(null!, () => new HtmlRubyVisitor()));
        Assert.Throws<ArgumentNullException>(() => engine.Render(new TextNode(""), null!));
        Assert.Throws<InvalidOperationException>(() => engine.Render(new TextNode(""), () => null!));
    }

    [Fact]
    public void InvalidKnowledgeData_IsRejected()
    {
        var builder = new GlossFilterPipelineBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.KnownWords(null!));
        Assert.Throws<ArgumentException>(() => builder.KnownWords(new[] { "" }));
        Assert.Throws<ArgumentNullException>(() => builder.KnownKanji(null!));
        Assert.Throws<ArgumentException>(() => builder.KnownKanji(new[] { new Rune('あ') }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Jlpt(0, new Dictionary<Rune, int>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Script((JapaneseScript)999));
    }

    [Fact]
    public async Task SessionEngineCache_TracksDictionaryAndOptionChanges()
    {
        var session = new PhoneticAnnotator.Wasm.AnnotationSession();
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("ja-JP\t猫\tねこ\n"));
        var initial = await session.AnnotateToHtml("猫", "{}");
        Assert.Equal(initial, await session.AnnotateToHtml("猫", "{}"));
        await session.LoadDictionaryChunk(Encoding.UTF8.GetBytes("ja-JP\t猫\tにゃんこ|ねこ\n"));
        Assert.DoesNotContain("<ruby>", await session.AnnotateToHtml("猫", "{}"));
        Assert.Contains("<ruby>", await session.AnnotateToHtml("猫", """{"pickFirstReading":true}"""));
        Assert.DoesNotContain("<ruby>", await session.AnnotateToHtml("猫", "{}"));
    }

    private static async Task<WordReadingLexicon> LoadWords(string tsv)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(tsv));
        return await WordReadingLexicon.LoadAsync(stream);
    }

    private sealed class CallbackFilter(Func<GlossAstNode, GlossAstNode> callback) : IGlossFilter
    {
        public GlossAstNode Apply(GlossAstNode node) => callback(node);
    }

    private sealed class CountingVisitor : IGlossAstVisitor
    {
        private int count;

        public void Visit(DocumentNode node)
        {
            foreach (var child in node.Children) child.Accept(this);
        }

        public void Visit(TextNode node) => count++;
        public void Visit(RubyNode node) => count++;
        public void Visit(UnresolvedGlossNode node) => count++;
        public void Visit(PronunciationNode node) => count++;
        public string GetResult() => count.ToString();
    }

    private sealed class CancellingAligner(CancellationTokenSource cancellation) : ISpanPhoneticAligner
    {
        public LanguageCode SupportedLanguage { get; } = new("ja-JP");

        public int AlignToSpan(ReadOnlySpan<char> baseText, ReadOnlySpan<char> fullReading,
            Span<GlossSegmentOffsets> destinationBuffer)
        {
            cancellation.Cancel();
            destinationBuffer[0] = new(0, baseText.Length, 0, fullReading.Length, false);
            return 1;
        }
    }
}
