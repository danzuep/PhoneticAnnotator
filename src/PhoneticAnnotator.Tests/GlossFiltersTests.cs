using System.Collections;
using System.Text;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class GlossFiltersTests
{
    private static RubyNode Japanese(string text, string reading = "にほん") =>
        new(text, reading, new LanguageCode("ja-JP"), ReadingSystem.JapaneseKana);

    [Fact]
    public void Pipeline_ComposesInOrderAndSharesUnmodifiedNestedNodes()
    {
        var known = Japanese("日");
        var unknown = Japanese("本");
        var text = new TextNode(" ");
        var untouched = new DocumentNode([text]);
        var nested = new DocumentNode([known, unknown]);
        var source = new DocumentNode([untouched, nested]);
        var pipeline = new GlossFilterPipeline(
            new UserKnownKanjiFilter(knownKanji: [new Rune('日')]),
            new ScriptFilter(JapaneseScript.Romaji));

        var result = Assert.IsType<DocumentNode>(pipeline.Apply(source));
        Assert.NotSame(source, result);
        Assert.Same(untouched, result.Children[0]);
        var changed = Assert.IsType<DocumentNode>(result.Children[1]);
        Assert.Equal(new TextNode("日"), changed.Children[0]);
        Assert.Equal("nihon", Assert.IsType<RubyNode>(changed.Children[1]).GlossText);
        Assert.Same(known, nested.Children[0]);
        Assert.Same(unknown, nested.Children[1]);
        Assert.Equal("にほん", unknown.GlossText);
        Assert.Equal("jpn-Kana", unknown.ReadingSystem.Tag);
        Assert.Same(source, new GlossFilterPipeline().Apply(source));
        Assert.Same(source, new UserKnownKanjiFilter().Apply(source));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<GlossAstNode>)result.Children)[0] = text);
    }

    [Fact]
    public void Pipeline_AppliesArbitraryLanguageIndependentFiltersInOrder()
    {
        var result = new GlossFilterPipeline(new AppendTextFilter("1"), new AppendTextFilter("2"))
            .Apply(new DocumentNode([new TextNode("x")]));
        Assert.Equal(new TextNode("x12"), Assert.IsType<DocumentNode>(result).Children[0]);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void Jlpt_UsesEasierOrEqualNumericLevels(int target, bool suppressed)
    {
        var filter = new JlptLevelFilter(target, new Dictionary<Rune, int>
        {
            [new Rune('日')] = 5, [new Rune('本')] = 3
        });
        var source = Japanese("日本");
        var result = filter.Apply(source);
        if (suppressed)
            Assert.Equal(new TextNode("日本"), result);
        else
            Assert.Same(source, result);
    }

    [Fact]
    public void Jlpt_HandlesOkuriganaAndSupplementaryKanjiConservatively()
    {
        var supplementary = new Rune(0x20000);
        var mapping = new Dictionary<Rune, int> { [new Rune('食')] = 5, [supplementary] = 4 };
        var filter = new JlptLevelFilter(4, mapping);
        mapping.Clear();
        Assert.Equal(new TextNode("食べる"), filter.Apply(Japanese("食べる")));
        Assert.Equal(new TextNode(supplementary + "べ"), filter.Apply(Japanese(supplementary + "べ")));
        foreach (var text in new[] { "食本", "かな", "カナ", "食A", "食々", "食\uFFFD" })
        {
            var source = Japanese(text);
            Assert.Same(source, filter.Apply(source));
        }
        var chinese = Japanese("食") with { Language = new LanguageCode("zh") };
        Assert.Same(chinese, filter.Apply(chinese));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Jlpt_RejectsInvalidTargetOrMappedLevels(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JlptLevelFilter(level, new Dictionary<Rune, int>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JlptLevelFilter(5, new Dictionary<Rune, int> { [new Rune('日')] = level }));
    }

    [Fact]
    public void Jlpt_RejectsNonKanjiKeys()
    {
        Assert.Throws<ArgumentException>(() =>
            new JlptLevelFilter(5, new Dictionary<Rune, int> { [new Rune('a')] = 5 }));
    }

    [Fact]
    public void Knowledge_UsesSnapshotOfExactWordsOrAllKnownKanji()
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "かな", "日本語" };
        var kanji = new HashSet<Rune> { new('食') };
        var filter = new UserKnownKanjiFilter(words, kanji);
        words.Clear();
        kanji.Clear();
        foreach (var text in new[] { "かな", "日本語", "食べる" })
            Assert.Equal(new TextNode(text), filter.Apply(Japanese(text)));
        foreach (var text in new[] { "カナ", "食本", "べる" })
        {
            var source = Japanese(text);
            Assert.Same(source, filter.Apply(source));
        }
        var chinese = Japanese("食") with { Language = new LanguageCode("cmn") };
        var arabic = new PronunciationNode("日本語", "reading", new LanguageCode("ar"), ReadingSystem.ArabicTashkeel);
        var unresolved = new UnresolvedGlossNode("食", ["しょく", "た"], new LanguageCode("ja"), ReadingSystem.JapaneseKana);
        Assert.Same(chinese, filter.Apply(chinese));
        Assert.Same(arabic, filter.Apply(arabic));
        Assert.Same(unresolved, filter.Apply(unresolved));
    }

    [Fact]
    public void Knowledge_BitArrayUsesUnicodeScalarIndicesAndCopiesInput()
    {
        var supplementary = new Rune(0x20000);
        var bits = new BitArray(supplementary.Value + 1);
        bits['日'] = true;
        bits[supplementary.Value] = true;
        var filter = new UserKnownKanjiFilter(null, bits);
        bits.SetAll(false);
        Assert.Equal(new TextNode("日" + supplementary), filter.Apply(Japanese("日" + supplementary)));
        var unknown = Japanese(new Rune(0x20001).ToString());
        Assert.Same(unknown, filter.Apply(unknown));
        Assert.Same(unknown, new UserKnownKanjiFilter(null, new BitArray(128)).Apply(unknown));
    }

    [Theory]
    [InlineData("きょう", "kyou")]
    [InlineData("しゅくだい", "shukudai")]
    [InlineData("ちゃわん", "chawan")]
    [InlineData("がっこう", "gakkou")]
    [InlineData("まっちゃ", "matcha")]
    [InlineData("いっしょ", "issho")]
    [InlineData("しんよう", "shin'you")]
    [InlineData("かんい", "kan'i")]
    [InlineData("こんにちは", "konnichiha")]
    [InlineData("センセイ", "sensei")]
    [InlineData("コーヒー", "koohii")]
    [InlineData("スーパー", "suupaa")]
    [InlineData("ティッシュ", "tisshu")]
    [InlineData("ファイル", "fairu")]
    [InlineData("ヴァイオリン", "vaiorin")]
    [InlineData("か\u3099", "ga")]
    [InlineData("ハ\u309A", "pa")]
    [InlineData("ウ\u3099", "vu")]
    public void Script_ConvertsHepburnWithoutChangingSource(string reading, string expected)
    {
        var source = Japanese("原文", reading);
        var result = Assert.IsType<RubyNode>(new ScriptFilter(JapaneseScript.Romaji).Apply(source));
        Assert.Equal(expected, result.GlossText);
        Assert.Equal("原文", result.BaseText);
        Assert.Equal(source.Language, result.Language);
        Assert.Equal("jpn-Latn", result.ReadingSystem.Tag);
        Assert.Equal(source.ReadingSystem.SourceField, result.ReadingSystem.SourceField);
        Assert.Equal(reading, source.GlossText);
    }

    [Theory]
    [InlineData(JapaneseScript.Hiragana, "ガッコウコーヒー", "がっこうこーひー", "jpn-Hira")]
    [InlineData(JapaneseScript.Katakana, "がっこうこーひー", "ガッコウコーヒー", "jpn-Kana")]
    public void Script_ConvertsKanaAndUpdatesMetadata(JapaneseScript script, string reading, string expected, string tag)
    {
        var result = Assert.IsType<RubyNode>(new ScriptFilter(script).Apply(Japanese("学校", reading)));
        Assert.Equal(expected, result.GlossText);
        Assert.Equal(tag, result.ReadingSystem.Tag);
    }

    [Theory]
    [InlineData("にほん?")]
    [InlineData("東京")]
    [InlineData("nihon")]
    [InlineData("ﾆﾎﾝ")]
    [InlineData("ー")]
    [InlineData("っ")]
    [InlineData("っあ")]
    [InlineData("んー")]
    [InlineData("っっか")]
    [InlineData("ゕ")]
    [InlineData("\u3099か")]
    [InlineData("か\u3099\u3099")]
    [InlineData("")]
    public void Script_UnsupportedReadingIsRetainedAtomically(string reading)
    {
        var source = Japanese("日本", reading);
        Assert.Same(source, new ScriptFilter(JapaneseScript.Romaji).Apply(source));
    }

    [Fact]
    public void Script_PreservesMalformedUtf16AndForeignReadings()
    {
        var malformed = Japanese("日本", "に\ud800");
        Assert.Same(malformed, new ScriptFilter(JapaneseScript.Romaji).Apply(malformed));
        var foreign = Japanese("日本", "ニホン") with { Language = new LanguageCode("zh") };
        foreach (var script in Enum.GetValues<JapaneseScript>())
            Assert.Same(foreign, new ScriptFilter(script).Apply(foreign));
    }

    [Fact]
    public void Script_ConvertsUnresolvedCandidatesAtomicallyAndJapanesePronunciation()
    {
        var filter = new ScriptFilter(JapaneseScript.Romaji);
        var source = new UnresolvedGlossNode("日", ["にち", "ひ"], new LanguageCode("jpn"), ReadingSystem.JapaneseKana);
        var result = Assert.IsType<UnresolvedGlossNode>(filter.Apply(source));
        Assert.Equal(["nichi", "hi"], result.ReadingCandidates);
        Assert.Equal("jpn-Latn", result.ReadingSystem.Tag);
        Assert.Equal(["にち", "ひ"], source.ReadingCandidates);
        var unsupported = source with { ReadingCandidates = ["にち", "hi"] };
        Assert.Same(unsupported, filter.Apply(unsupported));
        var empty = source with { ReadingCandidates = [] };
        Assert.Same(empty, filter.Apply(empty));
        var pronunciation = new PronunciationNode("日", "ひ", new LanguageCode("JA"), ReadingSystem.JapaneseKana);
        Assert.Equal("hi", Assert.IsType<PronunciationNode>(filter.Apply(pronunciation)).Pronunciation);
        Assert.Same(result, filter.Apply(result));
    }

    [Fact]
    public void Script_NoChangeRetainsOriginalNodes()
    {
        var source = Japanese("日", "ヒ");
        Assert.Same(source, new ScriptFilter(JapaneseScript.Katakana).Apply(source));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptFilter((JapaneseScript)42));
    }

    private sealed class AppendTextFilter(string suffix) : GlossFilter
    {
        protected override GlossAstNode Transform(GlossAstNode node) =>
            node is TextNode text ? text with { Value = text.Value + suffix } : node;
    }
}
