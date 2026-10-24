namespace PhoneticAnnotator.Core;

/// <summary>
/// Configures already-loaded immutable dictionaries. Word matching prefers the longest match,
/// then the last-added lexicon for ties. Unihan selects scalar-only tokenization and cannot
/// be combined with word lexicons. Loading and stream lifetime remain caller-owned.
/// </summary>
public sealed class AnnotationDictionaryBuilder
{
    private readonly List<WordReadingLexicon> lexicons = new();
    private UnihanReadingDictionary? unihan;

    public AnnotationDictionaryBuilder Words(params WordReadingLexicon[] lexicons)
    {
        ArgumentNullException.ThrowIfNull(lexicons);
        if (lexicons.Any(lexicon => lexicon is null))
            throw new ArgumentException("Word lexicons cannot contain null.", nameof(lexicons));
        this.lexicons.AddRange(lexicons);
        return this;
    }

    public AnnotationDictionaryBuilder Unihan(UnihanReadingDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        unihan = dictionary;
        return this;
    }

    internal (WordReadingLexicon[], UnihanReadingDictionary?) Snapshot(LanguageCode language, ReadingSystem reading)
    {
        if (unihan is not null)
        {
            if (lexicons.Count != 0)
                throw new InvalidOperationException("Choose either word lexicons or a scalar Unihan dictionary.");
            if (!string.Equals(unihan.Option.Language.Value, language.Value, StringComparison.OrdinalIgnoreCase)
                || !AnnotationEngineBuilder.EquivalentReading(unihan.Option.ReadingSystem, reading))
                throw new ArgumentException("The Unihan dictionary language and reading must match the engine.");
        }
        return (lexicons.ToArray(), unihan);
    }
}
