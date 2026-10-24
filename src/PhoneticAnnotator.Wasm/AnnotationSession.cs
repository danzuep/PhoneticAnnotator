using System.Text.Json;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Wasm;

public sealed class AnnotationSession
{
    private static readonly ISpanPhoneticAligner JapaneseAligner = new SpanCharacterAligner();
    private static readonly ReadingSystem ThaiReadingSystem = new(
        "th", "Thai", Unihan.Models.UnihanField.Unknown);
    private WordReadingLexicon[] _lexicons = [];
    private UnihanReadingDictionary? _unihan;

    public Task<string> AnnotateToHtml(string text, string optionsJson) =>
        AnnotateAsync(text, optionsJson, new HtmlRubyVisitor());

    public Task<string> AnnotateToJson(string text, string optionsJson) =>
        AnnotateAsync(text, optionsJson, new JsonAstVisitor());

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

    private Task<string> AnnotateAsync(string text, string optionsJson, IGlossAstVisitor visitor)
    {
        try
        {
            return Task.FromResult(Annotate(text, optionsJson, visitor));
        }
        catch (Exception exception)
        {
            // Retain the existing asynchronous API's faulted-task behavior.
            return Task.FromException<string>(exception);
        }
    }

    private string Annotate(string text, string optionsJson, IGlossAstVisitor visitor)
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
        ISpanPhoneticAligner? aligner = alignOkurigana && readingSystem == ReadingSystem.JapaneseKana
            ? JapaneseAligner
            : null;
        var snapshot = Volatile.Read(ref _lexicons);
        var document = GlossDocumentAssembler.Assemble(
            new TokenEnumerator(text.AsSpan(), snapshot, language, readingSystem, unihan),
            pickFirst,
            aligner);
        document.Accept(visitor);
        return visitor.GetResult();
    }

}
