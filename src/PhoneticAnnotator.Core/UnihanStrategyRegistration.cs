namespace PhoneticAnnotator.Core;

/// <summary>
/// Registers the built-in per-rune Unihan fallback strategies for supported CJK languages.
/// </summary>
public static class UnihanStrategyRegistration
{
    /// <summary>
    /// Registers Mandarin, Cantonese/Jyutping, and Japanese Unihan fallback strategies.
    /// </summary>
    /// <param name="factory">The strategy factory to populate.</param>
    /// <param name="readingProvider">The indexed Unihan reading provider.</param>
    public static void RegisterFallbacks(
        ITokenizerStrategyFactory factory,
        ICharacterReadingProvider readingProvider)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(readingProvider);

        factory.RegisterStrategy(new UnihanRuneStrategy(
            readingProvider,
            new LanguageCode("zh-CN"),
            ReadingSystem.MandarinPinyin));
        factory.RegisterStrategy(new UnihanRuneStrategy(
            readingProvider,
            new LanguageCode("zh-HK"),
            ReadingSystem.CantoneseJyutping));
        factory.RegisterStrategy(new UnihanRuneStrategy(
            readingProvider,
            new LanguageCode("ja-JP"),
            ReadingSystem.JapaneseKana));
    }
}