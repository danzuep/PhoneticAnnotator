using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Wasm;

public sealed class AnnotationSession
{
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

    private async Task<string> AnnotateAsync(string text, string optionsJson, IGlossAstVisitor visitor)
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
                _ => throw new ArgumentException($"Unsupported language '{languageTag}'.", nameof(optionsJson))
            };
        var selected = readingId == "ar" ? null : UnihanReadingOption.Get(readingId);
        var language = selected?.Language ?? new LanguageCode("ar");
        var readingSystem = selected?.ReadingSystem ?? ReadingSystem.ArabicTashkeel;
        var useUnihan = options.RootElement.TryGetProperty("strategy", out var strategyOption)
            && strategyOption.GetString() switch
        {
            "unihan" => true,
            "word" => false,
            _ => throw new ArgumentException("Strategy must be 'unihan' or 'word'.", nameof(optionsJson))
        };
        var unihan = useUnihan ? Volatile.Read(ref _unihan) : null;
        if (useUnihan && (unihan is null || !unihan.Option.Id.Equals(readingId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Load the selected Unihan dictionary before annotating.");
        var pickFirst = options.RootElement.TryGetProperty("pickFirstReading", out var pickFirstOption)
            && pickFirstOption.GetBoolean();
        var alignOkurigana = !options.RootElement.TryGetProperty("alignOkurigana", out var alignmentOption)
            || alignmentOption.GetBoolean();
        ISpanPhoneticAligner? aligner = alignOkurigana && readingSystem == ReadingSystem.JapaneseKana
            ? new JapaneseOkuriganaAligner()
            : null;
        var snapshot = Volatile.Read(ref _lexicons);
        var document = await GlossDocumentAssembler.AssembleAsync(
            TokenizeAsync(text.AsMemory(), snapshot, language, readingSystem, unihan),
            new CandidateOnlyDisambiguator(pickFirst),
            aligner).ConfigureAwait(false);
        document.Accept(visitor);
        return visitor.GetResult();
    }

    private static async IAsyncEnumerable<MemoryToken> TokenizeAsync(
        ReadOnlyMemory<char> text,
        WordReadingLexicon[] lexicons,
        LanguageCode language,
        ReadingSystem readingSystem,
        UnihanReadingDictionary? unihan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var offset = 0; offset < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = 0;
            IReadOnlyList<string> candidates = Array.Empty<string>();
            // Longest match wins; the most recently loaded chunk wins equal-length ties.
            foreach (var lexicon in unihan is null ? lexicons : Array.Empty<WordReadingLexicon>())
            {
                if (lexicon.TryGetLongestMatch(language, text.Span, offset, out var matchLength, out var readings)
                    && matchLength >= length)
                {
                    length = matchLength;
                    candidates = readings;
                }
            }

            if (length == 0)
            {
                if (Rune.DecodeFromUtf16(text.Span[offset..], out var rune, out length) != OperationStatus.Done)
                {
                    length = 1;
                }
                else if (unihan is not null)
                {
                    candidates = unihan.GetReadings(rune);
                }
            }

            yield return new MemoryToken(text.Slice(offset, length), offset, language, readingSystem, candidates);
            offset += length;
        }
    }
}
