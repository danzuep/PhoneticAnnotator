using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Tests;

public sealed class TokenizerStrategyFactoryTests
{
    [Fact]
    public void GetStrategy_WithoutPreference_ReturnsFirstRegisteredStrategy()
    {
        var factory = new TokenizerStrategyFactory();
        var first = new StubTokenizerStrategy("zh-CN", "fallback");
        factory.RegisterStrategy(first);
        factory.RegisterStrategy(new StubTokenizerStrategy("zh-CN", "dictionary"));

        var selected = factory.GetStrategy(new LanguageCode("zh-CN"));

        Assert.Same(first, selected);
    }

    [Fact]
    public void GetStrategy_WithPreference_SelectsMatchingStrategyCaseInsensitively()
    {
        var factory = new TokenizerStrategyFactory();
        var preferred = new StubTokenizerStrategy("zh-CN", "dictionary");
        factory.RegisterStrategy(new StubTokenizerStrategy("zh-CN", "fallback"));
        factory.RegisterStrategy(preferred);

        var selected = factory.GetStrategy(new LanguageCode("ZH-cn"), "DICTIONARY");

        Assert.Same(preferred, selected);
    }

    [Fact]
    public void GetStrategy_UnknownLanguage_ThrowsKeyNotFoundException()
    {
        var factory = new TokenizerStrategyFactory();

        Assert.Throws<KeyNotFoundException>(() => factory.GetStrategy(new LanguageCode("ar")));
    }

    [Fact]
    public void GetStrategy_UnknownPreference_ThrowsKeyNotFoundException()
    {
        var factory = new TokenizerStrategyFactory();
        factory.RegisterStrategy(new StubTokenizerStrategy("zh-CN", "fallback"));

        Assert.Throws<KeyNotFoundException>(() => factory.GetStrategy(new LanguageCode("zh-CN"), "missing"));
    }

    [Fact]
    public void RegisterStrategy_DuplicateIdentifierForLanguage_ThrowsInvalidOperationException()
    {
        var factory = new TokenizerStrategyFactory();
        factory.RegisterStrategy(new StubTokenizerStrategy("zh-CN", "fallback"));

        Assert.Throws<InvalidOperationException>(
            () => factory.RegisterStrategy(new StubTokenizerStrategy("ZH-cn", "FALLBACK")));
    }

    private sealed class StubTokenizerStrategy(string language, string strategyName) : ITokenizerStrategy
    {
        public LanguageCode SupportedLanguage { get; } = new(language);

        public string StrategyName { get; } = strategyName;

        public IAsyncEnumerable<MemoryToken> TokenizeAsync(
            ReadOnlyMemory<char> text,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}