using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using PhoneticAnnotator.Core;

namespace PhoneticAnnotator.Wasm;

public sealed class AnnotationSession
{
    private readonly SemaphoreSlim _dictionaryGate = new(1, 1);
    private WordReadingLexicon[] _lexicons = [];

    public Task<string> AnnotateToHtml(string text, string optionsJson) =>
        AnnotateAsync(text, optionsJson, new HtmlRubyVisitor());

    public Task<string> AnnotateToJson(string text, string optionsJson) =>
        AnnotateAsync(text, optionsJson, new JsonAstVisitor());

    public async Task LoadDictionaryChunk(byte[] dictionaryData)
    {
        ArgumentNullException.ThrowIfNull(dictionaryData);
        ReadOnlyMemory<byte> memory = dictionaryData;
        if (memory.IsEmpty)
        {
            throw new ArgumentException("A dictionary chunk must contain a complete UTF-8 TSV file.", nameof(dictionaryData));
        }

        await _dictionaryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // MemoryStream wraps the marshaled array without copying it.
            using var stream = new MemoryStream(dictionaryData, writable: false);
            var lexicon = await WordReadingLexicon.LoadAsync(stream).ConfigureAwait(false);
            var current = Volatile.Read(ref _lexicons);
            Volatile.Write(ref _lexicons, [.. current, lexicon]);
        }
        finally
        {
            _dictionaryGate.Release();
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
        var language = new LanguageCode(languageTag);
        var readingSystem = languageTag.ToLowerInvariant() switch
        {
            "ja-jp" => ReadingSystem.JapaneseKana,
            "zh-cn" => ReadingSystem.MandarinPinyin,
            "zh-hk" => ReadingSystem.CantoneseJyutping,
            "ar" or "ar-arab" => ReadingSystem.ArabicTashkeel,
            _ => throw new ArgumentException($"Unsupported language '{languageTag}'.", nameof(optionsJson))
        };
        var alignOkurigana = !options.RootElement.TryGetProperty("alignOkurigana", out var alignmentOption)
            || alignmentOption.GetBoolean();
        ISpanPhoneticAligner? aligner = alignOkurigana && readingSystem == ReadingSystem.JapaneseKana
            ? new JapaneseOkuriganaAligner()
            : null;
        var snapshot = Volatile.Read(ref _lexicons);
        var document = await GlossDocumentAssembler.AssembleAsync(
            TokenizeAsync(text.AsMemory(), snapshot, language, readingSystem),
            new CandidateOnlyDisambiguator(),
            aligner).ConfigureAwait(false);
        document.Accept(visitor);
        return visitor.GetResult();
    }

    private static async IAsyncEnumerable<MemoryToken> TokenizeAsync(
        ReadOnlyMemory<char> text,
        WordReadingLexicon[] lexicons,
        LanguageCode language,
        ReadingSystem readingSystem,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var offset = 0; offset < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = 0;
            IReadOnlyList<string> candidates = Array.Empty<string>();
            // Longest match wins; the most recently loaded chunk wins equal-length ties.
            foreach (var lexicon in lexicons)
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
                if (Rune.DecodeFromUtf16(text.Span[offset..], out _, out length) != OperationStatus.Done)
                {
                    length = 1;
                }
            }

            yield return new MemoryToken(text.Slice(offset, length), offset, language, readingSystem, candidates);
            offset += length;
        }
    }
}
