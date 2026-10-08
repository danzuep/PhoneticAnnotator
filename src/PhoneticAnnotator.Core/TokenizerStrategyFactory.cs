namespace PhoneticAnnotator.Core;

/// <summary>
/// Resolves tokenizer strategies using case-insensitive language tags and identifiers.
/// </summary>
public sealed class TokenizerStrategyFactory : ITokenizerStrategyFactory
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<ITokenizerStrategy>> _strategies = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ITokenizerStrategy GetStrategy(LanguageCode language, string? preferredStrategyName = null)
    {
        ArgumentNullException.ThrowIfNull(language);

        lock (_gate)
        {
            if (!_strategies.TryGetValue(language.Value, out var strategies))
            {
                throw new KeyNotFoundException($"No tokenizer strategy is registered for language '{language.Value}'.");
            }

            if (preferredStrategyName is null)
            {
                return strategies[0];
            }

            var preferred = strategies.FirstOrDefault(strategy =>
                string.Equals(strategy.StrategyName, preferredStrategyName, StringComparison.OrdinalIgnoreCase));

            return preferred ?? throw new KeyNotFoundException(
                $"No tokenizer strategy named '{preferredStrategyName}' is registered for language '{language.Value}'.");
        }
    }

    /// <inheritdoc />
    public void RegisterStrategy(ITokenizerStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(strategy.SupportedLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategy.StrategyName);

        lock (_gate)
        {
            if (!_strategies.TryGetValue(strategy.SupportedLanguage.Value, out var strategies))
            {
                strategies = [];
                _strategies.Add(strategy.SupportedLanguage.Value, strategies);
            }

            if (strategies.Any(existing =>
                string.Equals(existing.StrategyName, strategy.StrategyName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"A tokenizer strategy named '{strategy.StrategyName}' is already registered for language '{strategy.SupportedLanguage.Value}'.");
            }

            strategies.Add(strategy);
        }
    }
}