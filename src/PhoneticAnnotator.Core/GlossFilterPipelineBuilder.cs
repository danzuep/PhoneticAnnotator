using System.Text;

namespace PhoneticAnnotator.Core;

/// <summary>
/// An ordered, fluent filter configuration. Built-in helpers copy caller-owned knowledge data.
/// Custom filters must themselves be immutable; their instances are shared by built snapshots.
/// </summary>
public sealed class GlossFilterPipelineBuilder
{
    private readonly List<IGlossFilter> filters = new();

    public GlossFilterPipelineBuilder Add(IGlossFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filters.Add(filter);
        return this;
    }

    public GlossFilterPipelineBuilder Jlpt(int targetLevel, IReadOnlyDictionary<Rune, int> kanjiLevels) =>
        Add(new JlptLevelFilter(targetLevel, kanjiLevels));

    public GlossFilterPipelineBuilder KnownWords(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var snapshot = new HashSet<string>(words, StringComparer.Ordinal);
        if (snapshot.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Known words cannot contain null or empty entries.", nameof(words));
        return Add(new UserKnownKanjiFilter(snapshot));
    }

    public GlossFilterPipelineBuilder KnownKanji(IEnumerable<Rune> kanji)
    {
        ArgumentNullException.ThrowIfNull(kanji);
        var snapshot = new HashSet<Rune>(kanji);
        if (snapshot.Any(rune => !JapaneseFilterText.IsKanji(rune)))
            throw new ArgumentException("Known kanji must contain only kanji scalars.", nameof(kanji));
        return Add(new UserKnownKanjiFilter(knownKanji: snapshot));
    }

    public GlossFilterPipelineBuilder Script(JapaneseScript script) => Add(new ScriptFilter(script));

    /// <summary>Builds an independent pipeline snapshot for use outside an annotation engine.</summary>
    public GlossFilterPipeline Build() => new(Snapshot());

    internal IGlossFilter[] Snapshot() => filters.ToArray();
}
