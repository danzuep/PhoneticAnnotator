using System.Text.Json;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Wasm;

public sealed class AnnotationSession
{
    private static readonly ReadingSystem ThaiReadingSystem = new(
        "th", "Thai", Unihan.Models.UnihanField.Unknown);
    private WordReadingLexicon[] _lexicons = [];
    private UnihanReadingDictionary? _unihan;
    private EngineCache? _engineCache;

    public Task<string> AnnotateToHtml(string text, string optionsJson) =>
        AnnotateAsync(text, optionsJson, static () => new HtmlRubyVisitor());

    public Task<string> AnnotateToJson(string text, string optionsJson) =>
        AnnotateAsync(text, optionsJson, static () => new JsonAstVisitor());

    public async Task LoadUnihanDictionary(byte[] dictionaryData, string readingId)
    {
        ArgumentNullException.ThrowIfNull(dictionaryData);
        using var stream = new MemoryStream(dictionaryData, writable: false);
        var dictionary = await UnihanReadingDictionary.LoadAsync(stream, readingId).ConfigureAwait(false);
        Interlocked.Exchange(ref _unihan, dictionary);
    }

    public async Task LoadDictionaryChunk(byte[] dictionaryData)
    {
        ArgumentNullException.ThrowIfNull(dictionaryData);
        ReadOnlyMemory<byte> memory = dictionaryData;
        if (memory.IsEmpty)
        {
            throw new ArgumentException("A dictionary chunk must contain a complete UTF-8 TSV file.", nameof(dictionaryData));
        }

        // Parse once before publishing; a failed chunk never changes shared state.
        using var stream = new MemoryStream(dictionaryData, writable: false);
        var lexicon = await WordReadingLexicon.LoadAsync(stream).ConfigureAwait(false);
        while (true)
        {
            var current = Volatile.Read(ref _lexicons);
            WordReadingLexicon[] updated = [.. current, lexicon];
            if (ReferenceEquals(Interlocked.CompareExchange(ref _lexicons, updated, current), current))
            {
                return;
            }
        }
    }

    private Task<string> AnnotateAsync(string text, string optionsJson, Func<IGlossAstVisitor> visitorFactory)
    {
        try
        {
            return Task.FromResult(Annotate(text, optionsJson, visitorFactory));
        }
        catch (Exception exception)
        {
            // Retain the existing asynchronous API's faulted-task behavior.
            return Task.FromException<string>(exception);
        }
    }

    private string Annotate(string text, string optionsJson, Func<IGlossAstVisitor> visitorFactory)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(optionsJson);
        using var options = JsonDocument.Parse(optionsJson);
        if (options.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Options must be a JSON object.", nameof(optionsJson));
        }

        var languageTag = options.RootElement.TryGetProperty("language", out var languageOption)
            ? languageOption.GetString() ?? throw new ArgumentException("Language cannot be null.", nameof(optionsJson))
            : "ja-JP";
        var readingId = options.RootElement.TryGetProperty("readingSystem", out var readingOption)
            ? readingOption.GetString() ?? throw new ArgumentException("Reading system cannot be null.", nameof(optionsJson))
            : languageTag.ToLowerInvariant() switch
            {
                "ja-jp" => "kJapanese", "zh-cn" => "kMandarin", "zh-hk" => "kCantonese",
                "ko" => "kHangul", "vi" => "kVietnamese", "za" => "kZhuang",
                "ar" or "ar-arab" => "ar",
                "th" or "th-th" => "th",
                _ => throw new ArgumentException($"Unsupported language '{languageTag}'.", nameof(optionsJson))
            };
        var selected = readingId is "ar" or "th" ? null : UnihanReadingOption.Get(readingId);
        readingId = selected?.Id ?? readingId;
        var language = selected?.Language ?? new LanguageCode(languageTag);
        var readingSystem = selected?.ReadingSystem
            ?? (readingId == "th" ? ThaiReadingSystem : ReadingSystem.ArabicTashkeel);
        var useUnihan = options.RootElement.TryGetProperty("strategy", out var strategyOption)
            && strategyOption.GetString() switch
        {
            "unihan" => true,
            "word" => false,
            _ => throw new ArgumentException("Strategy must be 'unihan' or 'word'.", nameof(optionsJson))
        };
        if (!useUnihan)
        {
            if (readingId is not ("kJapanese" or "kMandarin" or "kCantonese" or "kHangul" or "kVietnamese" or "kZhuang" or "ar" or "th"))
                throw new ArgumentException("Alternate reading fields require the Unihan strategy.", nameof(optionsJson));
            if (selected is not null && !languageTag.Equals(selected.Language.Value, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Word dictionary language must match the reading system.", nameof(optionsJson));
            if (readingId == "ar" && !languageTag.Equals("ar", StringComparison.OrdinalIgnoreCase)
                && !languageTag.Equals("ar-Arab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Arabic word dictionaries require language 'ar' or 'ar-Arab'.", nameof(optionsJson));
            if (readingId == "th" && !languageTag.Equals("th", StringComparison.OrdinalIgnoreCase)
                && !languageTag.Equals("th-TH", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Thai word dictionaries require language 'th' or 'th-TH'.", nameof(optionsJson));
            language = new LanguageCode(languageTag);
        }
        var unihan = useUnihan ? Volatile.Read(ref _unihan) : null;
        if (useUnihan && (unihan is null || !unihan.Option.Id.Equals(readingId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Load the selected Unihan dictionary before annotating.");
        var pickFirst = options.RootElement.TryGetProperty("pickFirstReading", out var pickFirstOption)
            && pickFirstOption.GetBoolean();
        var alignOkurigana = !options.RootElement.TryGetProperty("alignOkurigana", out var alignmentOption)
            || alignmentOption.GetBoolean();
        var snapshot = Volatile.Read(ref _lexicons);
        var cached = Volatile.Read(ref _engineCache);
        AnnotationEngine engine;
        if (cached is not null
            && ReferenceEquals(cached.Lexicons, snapshot)
            && ReferenceEquals(cached.Unihan, unihan)
            && cached.AlignOkurigana == alignOkurigana
            && cached.Engine.PickFirstReading == pickFirst
            && cached.Engine.ReadingSystem == readingSystem
            && string.Equals(cached.Engine.Language.Value, language.Value, StringComparison.OrdinalIgnoreCase))
        {
            engine = cached.Engine;
        }
        else
        {
            engine = BuildEngine(language, readingSystem, pickFirst, alignOkurigana, snapshot, unihan);
            // A single-entry cache bounds retained dictionary snapshots. Racing callers may
            // replace the cache, but each request keeps its own consistent immutable engine.
            Volatile.Write(ref _engineCache, new EngineCache(snapshot, unihan, alignOkurigana, engine));
        }
        return engine.Render(engine.Annotate(text.AsSpan()), visitorFactory);
    }

    private static AnnotationEngine BuildEngine(
        LanguageCode language,
        ReadingSystem readingSystem,
        bool pickFirst,
        bool alignOkurigana,
        WordReadingLexicon[] lexicons,
        UnihanReadingDictionary? unihan)
    {
        var builder = new AnnotationEngineBuilder();
        builder.Language(language).Reading(readingSystem).PickFirstReading(pickFirst)
            .Dictionaries(dictionaries =>
            {
                if (unihan is not null) dictionaries.Unihan(unihan);
                else dictionaries.Words(lexicons);
            });
        if (!alignOkurigana) builder.WithoutAlignment();
        return builder.Build();
    }

    private sealed record EngineCache(
        WordReadingLexicon[] Lexicons,
        UnihanReadingDictionary? Unihan,
        bool AlignOkurigana,
        AnnotationEngine Engine);
}
