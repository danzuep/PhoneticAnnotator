using System.Collections;
using System.Text;

namespace PhoneticAnnotator.Core;

/// <summary>An immutable AST transformation, independent of tokenizers and rendering visitors.</summary>
public interface IGlossFilter
{
    /// <summary>Transforms a node, retaining the original instance when nothing changes.</summary>
    GlossAstNode Apply(GlossAstNode node);
}

/// <summary>Applies filters in caller-specified order. An empty pipeline is the identity transform.</summary>
public sealed class GlossFilterPipeline : IGlossFilter
{
    private readonly IGlossFilter[] filters;

    /// <summary>Creates a pipeline, taking a snapshot of the supplied filters.</summary>
    public GlossFilterPipeline(params IGlossFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Any(filter => filter is null))
            throw new ArgumentException("Filters cannot contain null.", nameof(filters));
        this.filters = (IGlossFilter[])filters.Clone();
    }

    /// <inheritdoc />
    public GlossAstNode Apply(GlossAstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var filter in filters)
            node = filter.Apply(node);
        return node;
    }
}

/// <summary>Recursively transforms documents using structural sharing of unchanged nodes.</summary>
public abstract class GlossFilter : IGlossFilter
{
    /// <inheritdoc />
    public GlossAstNode Apply(GlossAstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is not DocumentNode document)
            return Transform(node);

        GlossAstNode[]? children = null;
        for (var index = 0; index < document.Children.Count; index++)
        {
            var original = document.Children[index];
            var transformed = Apply(original);
            if (!ReferenceEquals(original, transformed))
                children ??= document.Children.ToArray();
            if (children is not null)
                children[index] = transformed;
        }
        return children is null ? document : new DocumentNode(Array.AsReadOnly(children));
    }

    /// <summary>Transforms a non-document node.</summary>
    protected abstract GlossAstNode Transform(GlossAstNode node);
}

/// <summary>
/// Suppresses Japanese ruby when every kanji has a supplied level at or easier than the target.
/// N5 is easiest; N1 is hardest. No official JLPT kanji dataset is assumed or included.
/// Kana/okurigana are ignored, but at least one kanji is required; unmapped kanji and other
/// base characters conservatively retain the annotation. Supplementary CJK scalars are supported.
/// </summary>
public sealed class JlptLevelFilter : GlossFilter
{
    private readonly Dictionary<Rune, int> levels;
    private readonly int targetLevel;
    private readonly Func<Rune, bool> matchesLevel;

    /// <summary>Creates a filter using caller-owned data; keys and levels are copied and validated.</summary>
    /// <param name="targetLevel">An integer from 1 (N1) to 5 (N5).</param>
    /// <param name="kanjiLevels">Kanji scalar to level (1 through 5).</param>
    public JlptLevelFilter(int targetLevel, IReadOnlyDictionary<Rune, int> kanjiLevels)
    {
        ValidateLevel(targetLevel);
        ArgumentNullException.ThrowIfNull(kanjiLevels);
        levels = new Dictionary<Rune, int>();
        foreach (var (kanji, level) in kanjiLevels)
        {
            ValidateLevel(level);
            if (!JapaneseFilterText.IsKanji(kanji))
                throw new ArgumentException("Mapping keys must be kanji.", nameof(kanjiLevels));
            levels.Add(kanji, level);
        }
        this.targetLevel = targetLevel;
        matchesLevel = MatchesLevel;
    }

    /// <inheritdoc />
    protected override GlossAstNode Transform(GlossAstNode node) =>
        node is RubyNode ruby && JapaneseFilterText.IsJapanese(ruby.Language) &&
        JapaneseFilterText.AllKanjiMatch(ruby.BaseText, matchesLevel)
            ? new TextNode(ruby.BaseText)
            : node;

    private bool MatchesLevel(Rune rune) =>
        levels.TryGetValue(rune, out var level) && level >= targetLevel;

    private static void ValidateLevel(int level)
    {
        if (level is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(level), "JLPT levels must be N1 through N5 (1–5).");
    }
}

/// <summary>
/// Suppresses only Japanese ruby for an exact known word or a base whose kanji are all known.
/// Empty defaults are a no-op. Kana/okurigana may accompany known kanji; kana-only bases are
/// suppressed only by an exact word match. Input sets and bit arrays are copied.
/// </summary>
public sealed class UserKnownKanjiFilter : GlossFilter
{
    private readonly HashSet<string> words;
    private readonly HashSet<Rune>? kanji;
    private readonly BitArray? kanjiBits;
    private readonly Func<Rune, bool> isKnown;

    /// <summary>Creates an optional knowledge filter using ordinal, case-sensitive word matching.</summary>
    public UserKnownKanjiFilter(HashSet<string>? knownWords = null, HashSet<Rune>? knownKanji = null)
    {
        words = new HashSet<string>(knownWords ?? [], StringComparer.Ordinal);
        kanji = new HashSet<Rune>(knownKanji ?? []);
        isKnown = IsKnown;
    }

    /// <summary>
    /// Creates a knowledge filter whose bit indices are Unicode scalar values, not UTF-16 code units.
    /// Out-of-range indices are unknown, allowing either BMP-sized or full-Unicode arrays.
    /// </summary>
    public UserKnownKanjiFilter(HashSet<string>? knownWords, BitArray knownKanji)
    {
        ArgumentNullException.ThrowIfNull(knownKanji);
        words = new HashSet<string>(knownWords ?? [], StringComparer.Ordinal);
        kanjiBits = new BitArray(knownKanji);
        isKnown = IsKnown;
    }

    /// <inheritdoc />
    protected override GlossAstNode Transform(GlossAstNode node) =>
        node is RubyNode ruby && JapaneseFilterText.IsJapanese(ruby.Language) &&
        (words.Contains(ruby.BaseText) || JapaneseFilterText.AllKanjiMatch(ruby.BaseText, isKnown))
            ? new TextNode(ruby.BaseText)
            : node;

    private bool IsKnown(Rune rune) =>
        kanji?.Contains(rune) ?? (rune.Value < kanjiBits!.Length && kanjiBits[rune.Value]);
}

/// <summary>The target script of Japanese readings.</summary>
public enum JapaneseScript
{
    /// <summary>Hiragana (Hira).</summary>
    Hiragana,
    /// <summary>Katakana (Kana).</summary>
    Katakana,
    /// <summary>Hepburn-style Latin transcription (Latn).</summary>
    Romaji
}

/// <summary>
/// Converts Japanese kana readings to hiragana, katakana, or Hepburn-style romaji.
/// Supported combining kana voicing marks are composed; romanized input, half-width kana and unsupported characters
/// are retained unchanged, including metadata, rather than guessed or partially converted.
/// Romaji preserves written vowel sequences (おう = ou, えい = ei); ー repeats the preceding
/// vowel, without macrons. Sokuon doubles the next consonant (ch becomes tch); moraic n uses
/// an apostrophe before vowels or y. Unsupported combinations retain the entire annotation.
/// All candidates of an unresolved node must convert successfully, or the entire node is retained.
/// Non-Japanese annotations and base/source text are never changed.
/// </summary>
public sealed class ScriptFilter : GlossFilter
{
    private readonly JapaneseScript script;

    /// <summary>Creates a Japanese reading-script filter.</summary>
    public ScriptFilter(JapaneseScript script)
    {
        if (!Enum.IsDefined(script))
            throw new ArgumentOutOfRangeException(nameof(script));
        this.script = script;
    }

    /// <inheritdoc />
    protected override GlossAstNode Transform(GlossAstNode node)
    {
        switch (node)
        {
            case RubyNode ruby when JapaneseFilterText.IsJapanese(ruby.Language):
                if (JapaneseReadingConversion.TryConvert(ruby.GlossText, script, out var gloss))
                {
                    var system = ConvertSystem(ruby.ReadingSystem);
                    return gloss == ruby.GlossText && system == ruby.ReadingSystem
                        ? ruby : ruby with { GlossText = gloss, ReadingSystem = system };
                }
                break;
            case UnresolvedGlossNode unresolved when JapaneseFilterText.IsJapanese(unresolved.Language):
                if (unresolved.ReadingCandidates.Count == 0)
                    break;
                var candidates = new string[unresolved.ReadingCandidates.Count];
                for (var index = 0; index < candidates.Length; index++)
                    if (!JapaneseReadingConversion.TryConvert(unresolved.ReadingCandidates[index], script, out candidates[index]))
                        return node;
                var candidateSystem = ConvertSystem(unresolved.ReadingSystem);
                return candidates.SequenceEqual(unresolved.ReadingCandidates) && candidateSystem == unresolved.ReadingSystem
                    ? node : unresolved with { ReadingCandidates = Array.AsReadOnly(candidates), ReadingSystem = candidateSystem };
            case PronunciationNode pronunciation when JapaneseFilterText.IsJapanese(pronunciation.Language):
                if (JapaneseReadingConversion.TryConvert(pronunciation.Pronunciation, script, out var reading))
                {
                    var system = ConvertSystem(pronunciation.ReadingSystem);
                    return reading == pronunciation.Pronunciation && system == pronunciation.ReadingSystem
                        ? node : pronunciation with { Pronunciation = reading, ReadingSystem = system };
                }
                break;
        }
        return node;
    }

    private ReadingSystem ConvertSystem(ReadingSystem system)
    {
        var tag = script switch
        {
            JapaneseScript.Hiragana => "jpn-Hira",
            JapaneseScript.Katakana => "jpn-Kana",
            _ => "jpn-Latn"
        };
        return system.Tag == tag ? system : system with
        {
            Tag = tag,
            Name = script == JapaneseScript.Romaji ? "Japanese Romaji" : $"Japanese {script}"
        };
    }
}

internal static class JapaneseFilterText
{
    internal static bool IsJapanese(LanguageCode language)
    {
        var tag = language.Value;
        var separator = tag.IndexOf('-');
        var primary = separator < 0 ? tag.AsSpan() : tag.AsSpan(0, separator);
        return primary.Equals("ja", StringComparison.OrdinalIgnoreCase) ||
               primary.Equals("jpn", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsKanji(Rune rune) => rune.Value is
        >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or
        >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x2A6DF or
        >= 0x2A700 and <= 0x2B73F or >= 0x2B740 and <= 0x2B81F or
        >= 0x2B820 and <= 0x2CEAF or >= 0x2CEB0 and <= 0x2EBEF or
        >= 0x2EBF0 and <= 0x2EE5F or >= 0x2F800 and <= 0x2FA1F or
        >= 0x30000 and <= 0x3134F or >= 0x31350 and <= 0x323AF or
        >= 0x323B0 and <= 0x3347F;

    internal static bool AllKanjiMatch(string text, Func<Rune, bool> predicate)
    {
        var foundKanji = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsKanji(rune))
            {
                foundKanji = true;
                if (!predicate(rune))
                    return false;
            }
            else if (rune.Value is not (>= 0x3041 and <= 0x3096 or >= 0x30A1 and <= 0x30FA or 0x30FC))
                return false;
        }
        return foundKanji;
    }
}
