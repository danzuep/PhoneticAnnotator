using System.Text;

namespace PhoneticAnnotator.Core;

/// <summary>
/// A read-only longest-match trie loaded from a caller-supplied language/word/reading TSV stream.
/// </summary>
public sealed class WordReadingLexicon
{
    private static readonly IReadOnlyList<string> NoCandidates = Array.Empty<string>();

    private readonly Dictionary<string, TrieNode> _roots = new(StringComparer.OrdinalIgnoreCase);

    private WordReadingLexicon()
    {
    }

    /// <summary>
    /// Loads entries in <c>language&lt;TAB&gt;word&lt;TAB&gt;reading1|reading2</c> format.
    /// Blank lines and lines beginning with <c>#</c> are ignored. The stream remains open.
    /// </summary>
    /// <param name="stream">A readable TSV stream.</param>
    /// <param name="cancellationToken">A token used to cancel loading.</param>
    /// <returns>A trie containing the supplied words and reading candidates.</returns>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="FormatException">An entry is malformed or contains invalid UTF-16.</exception>
    public static async Task<WordReadingLexicon> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The lexicon stream must be readable.", nameof(stream));
        }

        var lexicon = new WordReadingLexicon();
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        var lineNumber = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[0]))
            {
                throw new FormatException($"Invalid word lexicon entry on line {lineNumber}.");
            }

            var word = fields[1];
            var candidates = fields[2]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (candidates.Length == 0 || !IsWellFormedUtf16(word) || candidates.Any(candidate => !IsWellFormedUtf16(candidate)))
            {
                throw new FormatException($"Invalid word or reading candidate on line {lineNumber}.");
            }

            try
            {
                _ = new LanguageCode(fields[0]);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException($"Invalid language tag on word lexicon line {lineNumber}.", exception);
            }

            lexicon.AddWord(fields[0], word, candidates);
        }

        return lexicon;
    }

    /// <summary>
    /// Finds the longest word beginning at an input UTF-16 offset.
    /// </summary>
    /// <param name="language">The language whose entries should be used.</param>
    /// <param name="text">The source text being tokenized.</param>
    /// <param name="startIndex">The UTF-16 offset where matching begins.</param>
    /// <param name="matchedLength">The matched word length, or zero when there is no match.</param>
    /// <param name="readingCandidates">The readings registered for the longest match.</param>
    /// <returns>True when a word matches at the specified offset.</returns>
    public bool TryGetLongestMatch(
        LanguageCode language,
        ReadOnlySpan<char> text,
        int startIndex,
        out int matchedLength,
        out IReadOnlyList<string> readingCandidates)
    {
        ArgumentNullException.ThrowIfNull(language);
        if ((uint)startIndex >= (uint)text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        matchedLength = 0;
        readingCandidates = NoCandidates;
        if (!_roots.TryGetValue(language.Value, out var current))
        {
            return false;
        }

        for (var index = startIndex; index < text.Length; index++)
        {
            if (!current.Children.TryGetValue(text[index], out current!))
            {
                break;
            }

            if (current.ReadingCandidates is not null)
            {
                matchedLength = index - startIndex + 1;
                readingCandidates = current.ReadingCandidates;
            }
        }

        return matchedLength > 0;
    }

    private void AddWord(string language, string word, IEnumerable<string> candidates)
    {
        if (!_roots.TryGetValue(language, out var current))
        {
            current = new TrieNode();
            _roots.Add(language, current);
        }

        foreach (var character in word)
        {
            if (!current.Children.TryGetValue(character, out var child))
            {
                child = new TrieNode();
                current.Children.Add(character, child);
            }

            current = child;
        }

        var merged = current.ReadingCandidates?.ToList() ?? [];
        foreach (var candidate in candidates)
        {
            if (!merged.Contains(candidate, StringComparer.Ordinal))
            {
                merged.Add(candidate);
            }
        }

        current.ReadingCandidates = Array.AsReadOnly(merged.ToArray());
    }

    private static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length;)
        {
            if (Rune.DecodeFromUtf16(value.AsSpan(index), out _, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                return false;
            }

            index += consumed;
        }

        return value.Length > 0;
    }

    private sealed class TrieNode
    {
        public Dictionary<char, TrieNode> Children { get; } = new();

        public IReadOnlyList<string>? ReadingCandidates { get; set; }
    }
}