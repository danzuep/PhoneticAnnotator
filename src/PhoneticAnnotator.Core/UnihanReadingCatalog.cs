using Unihan.Models;

namespace PhoneticAnnotator.Core;

/// <summary>Modern pronunciation fields available in the bundled Unicode Unihan data.</summary>
public sealed record UnihanReadingOption(string Id, LanguageCode Language, ReadingSystem ReadingSystem)
{
    public static IReadOnlyList<UnihanReadingOption> All { get; } = Array.AsReadOnly(new[]
    {
        new UnihanReadingOption("kMandarin", new("zh-CN"), ReadingSystem.MandarinPinyin),
        new UnihanReadingOption("kCantonese", new("zh-HK"), ReadingSystem.CantoneseJyutping),
        new UnihanReadingOption("kJapanese", new("ja-JP"), ReadingSystem.JapaneseKana),
        new UnihanReadingOption("kJapaneseOn", new("ja-JP"), new("ja-Latn", "Japanese On (romanized)", UnihanField.kJapaneseOn)),
        new UnihanReadingOption("kJapaneseKun", new("ja-JP"), new("ja-Latn", "Japanese Kun (romanized)", UnihanField.kJapaneseKun)),
        new UnihanReadingOption("kHangul", new("ko"), new("ko-Hang", "Korean Hangul", UnihanField.kHangul)),
        new UnihanReadingOption("kKorean", new("ko"), new("ko-Latn", "Korean (romanized)", UnihanField.kKorean)),
        new UnihanReadingOption("kVietnamese", new("vi"), new("vi-Latn", "Vietnamese", UnihanField.kVietnamese)),
        new UnihanReadingOption("kZhuang", new("za"), new("za-Latn", "Zhuang", UnihanField.kZhuang)),
        new UnihanReadingOption("kHanyuPinlu", new("zh-CN"), new("cmn-Latn", "Mandarin frequency readings", UnihanField.kHanyuPinlu)),
        new UnihanReadingOption("kHanyuPinyin", new("zh-CN"), new("cmn-Latn", "Hanyu Da Zidian Pinyin", UnihanField.kHanyuPinyin)),
        new UnihanReadingOption("kTGHZ2013", new("zh-CN"), new("cmn-Latn", "TGHZ 2013 Pinyin", UnihanField.kTGHZ2013)),
        new UnihanReadingOption("kXHC1983", new("zh-CN"), new("cmn-Latn", "XHC 1983 Pinyin", UnihanField.kXHC1983)),
        new UnihanReadingOption("kSMSZD2003Readings", new("zh-CN"), new("cmn-Latn", "SMSZD 2003 Mandarin", UnihanField.kSMSZD2003Readings)),
        new UnihanReadingOption("kSMSZD2003Readings-Cantonese", new("zh-HK"), new("yue-Latn", "SMSZD 2003 Cantonese", UnihanField.kSMSZD2003Readings))
    });

    public static UnihanReadingOption Get(string id) =>
        All.FirstOrDefault(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unsupported Unihan reading system '{id}'.", nameof(id));
}
