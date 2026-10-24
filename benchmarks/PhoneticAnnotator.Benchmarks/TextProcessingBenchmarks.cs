using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Benchmarks;

/// <summary>Identical dictionaries and source text compare borrowed enumeration with the async API.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TextProcessingBenchmarks
{
    private string _text = "";
    private WordLexiconTokenizerStrategy _strategy = null!;
    private WordReadingLexicon[] _lexicons = [];
    private LanguageCode _language = null!;
    private ReadingSystem _readingSystem = null!;
    private readonly CandidateOnlyDisambiguator _disambiguator = new();

    [Params("ja-JP", "zh-CN", "ko", "th")]
    public string Language { get; set; } = "ja-JP";

    [Params("Tweet", "Article", "Chapter")]
    public string InputSize { get; set; } = "Tweet";

    [GlobalSetup]
    public async Task Setup()
    {
        var (sentence, word, reading, system) = Language switch
        {
            "ja-JP" => ("今日は学校で日本語を勉強します。", "学校", "がっこう", ReadingSystem.JapaneseKana),
            "zh-CN" => ("今天我们在学校学习中文。", "学校", "xué xiào", ReadingSystem.MandarinPinyin),
            "ko" => ("오늘 學校에서 한국어를 공부합니다. ", "學校", "학교",
                new ReadingSystem("ko-Hang", "Hangul", Unihan.Models.UnihanField.kHangul)),
            _ => ("วันนี้เราเรียนภาษาไทยที่โรงเรียน ", "โรงเรียน", "roong riian",
                new ReadingSystem("th-Latn", "Thai romanization", Unihan.Models.UnihanField.Unknown))
        };
        var repetitions = InputSize switch { "Tweet" => 4, "Article" => 128, _ => 2048 };
        // Only setup allocates source strings and indexes dictionary entries.
        _text = string.Concat(Enumerable.Repeat(sentence, repetitions));
        _language = new LanguageCode(Language);
        _readingSystem = system;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"{Language}\t{word}\t{reading}\n"));
        var lexicon = await WordReadingLexicon.LoadAsync(stream);
        _lexicons = [lexicon];
        _strategy = new WordLexiconTokenizerStrategy(lexicon, _language, system);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Tokenization")]
    public async Task<int> AsyncTokens()
    {
        var checksum = 0;
        await foreach (var token in _strategy.TokenizeAsync(_text.AsMemory()))
            checksum += token.Text.Length + token.ReadingCandidates.Count;
        return checksum;
    }

    [Benchmark]
    [BenchmarkCategory("Tokenization")]
    public int SpanTokens()
    {
        var checksum = 0;
        foreach (var token in new TokenEnumerator(_text, _lexicons, _language, _readingSystem))
            checksum += token.Text.Length + token.ReadingCandidates.Count;
        return checksum;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("AST")]
    public Task<DocumentNode> AsyncDocument() =>
        GlossDocumentAssembler.AssembleAsync(_strategy.TokenizeAsync(_text.AsMemory()), _disambiguator);

    [Benchmark]
    [BenchmarkCategory("AST")]
    public DocumentNode SpanDocument() =>
        GlossDocumentAssembler.Assemble(new TokenEnumerator(_text, _lexicons, _language, _readingSystem));
}
