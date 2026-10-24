using Unihan.Models;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Fluent, reusable engine configuration. Build snapshots all builder collections.
/// Language and reading are required; no dictionaries means unannotated text.
/// Japanese kana uses okurigana alignment by default; other readings use whole-token ruby.
/// </summary>
public sealed class AnnotationEngineBuilder
{
    private LanguageCode? language;
    private ReadingSystem? readingSystem;
    private bool pickFirstReading;
    private bool automaticAlignment = true;
    private ISpanPhoneticAligner? aligner;
    private readonly AnnotationDictionaryBuilder dictionaries = new();
    private readonly GlossFilterPipelineBuilder filters = new();

    public AnnotationEngineBuilder Language(string language) => Language(new LanguageCode(language));

    public AnnotationEngineBuilder Language(LanguageCode language)
    {
        ArgumentNullException.ThrowIfNull(language);
        this.language = language;
        return this;
    }

    /// <summary>
    /// Selects a reading. Catalog-backed fields require a compatible language, tag and
    /// annotation kind, but the display name is freely customizable. Unknown source fields
    /// support custom readings whose tag shares the input language's primary subtag.
    /// </summary>
    public AnnotationEngineBuilder Reading(ReadingSystem readingSystem)
    {
        ArgumentNullException.ThrowIfNull(readingSystem);
        this.readingSystem = readingSystem;
        return this;
    }

    /// <summary>Selects both the language and reading from a named Unihan catalog entry.</summary>
    public AnnotationEngineBuilder Reading(string unihanReadingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unihanReadingId);
        var option = UnihanReadingOption.Get(unihanReadingId);
        return Language(option.Language).Reading(option.ReadingSystem);
    }

    /// <summary>False (the default) preserves multiple candidates as unresolved annotations.</summary>
    public AnnotationEngineBuilder PickFirstReading(bool enabled = true)
    {
        pickFirstReading = enabled;
        return this;
    }

    /// <summary>Uses a custom synchronous aligner instead of automatic alignment.</summary>
    public AnnotationEngineBuilder Alignment(ISpanPhoneticAligner aligner)
    {
        ArgumentNullException.ThrowIfNull(aligner);
        this.aligner = aligner;
        automaticAlignment = false;
        return this;
    }

    /// <summary>Disables alignment, keeping resolved annotations at whole-token granularity.</summary>
    public AnnotationEngineBuilder WithoutAlignment()
    {
        aligner = null;
        automaticAlignment = false;
        return this;
    }

    /// <summary>Adds dictionary configuration. Repeated calls append word lexicons.</summary>
    public AnnotationEngineBuilder Dictionaries(Action<AnnotationDictionaryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(dictionaries);
        return this;
    }

    /// <summary>Adds filters in order. Repeated calls append to the pipeline.</summary>
    public AnnotationEngineBuilder Filters(Action<GlossFilterPipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(filters);
        return this;
    }

    public AnnotationEngine Build()
    {
        var selectedLanguage = language ?? throw new InvalidOperationException("Configure a language before building.");
        var selectedReading = readingSystem ?? throw new InvalidOperationException("Configure a reading before building.");
        ValidateReading(selectedLanguage, selectedReading);
        var selectedAligner = automaticAlignment && EquivalentReading(selectedReading, ReadingSystem.JapaneseKana)
            ? new SpanCharacterAligner()
            : aligner;
        if (selectedAligner is not null)
        {
            if (selectedReading.AnnotationKind != ReadingAnnotationKind.Ruby)
                throw new ArgumentException("Alignment requires a ruby reading system.");
            if (!string.Equals(selectedLanguage.Value, selectedAligner.SupportedLanguage?.Value, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The aligner language must match the engine language.");
            if (selectedAligner is SpanCharacterAligner or JapaneseOkuriganaAligner
                && !EquivalentReading(selectedReading, ReadingSystem.JapaneseKana))
                throw new ArgumentException("Japanese okurigana alignment requires Japanese kana readings.");
        }
        var (lexicons, unihan) = dictionaries.Snapshot(selectedLanguage, selectedReading);
        return new AnnotationEngine(selectedLanguage, selectedReading, pickFirstReading,
            lexicons, unihan, selectedAligner, filters.Snapshot());
    }

    private static void ValidateReading(LanguageCode language, ReadingSystem reading)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reading.Tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(reading.Name);
        if (!Enum.IsDefined(reading.AnnotationKind) || !Enum.IsDefined(reading.SourceField))
            throw new ArgumentException("The reading system contains an unsupported annotation kind or source field.");
        if (reading.SourceField != UnihanField.Unknown)
        {
            if (!UnihanReadingOption.All.Any(option =>
                EquivalentReading(option.ReadingSystem, reading) &&
                string.Equals(option.Language.Value, language.Value, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("The reading system is unsupported or incompatible with the language.");
        }
        else
        {
            var languageRoot = language.Value.Split('-')[0];
            var readingRoot = reading.Tag.Split('-')[0];
            if (!string.Equals(languageRoot, readingRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A custom reading tag must share the input language's primary subtag.");
        }

    }

    internal static bool EquivalentReading(ReadingSystem left, ReadingSystem right) =>
        left.SourceField == right.SourceField
        && left.AnnotationKind == right.AnnotationKind
        && string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase);
}
