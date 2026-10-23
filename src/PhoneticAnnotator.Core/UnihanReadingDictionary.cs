using System.Globalization;
using System.Text;
using Unihan.Models;

namespace PhoneticAnnotator.Core;

/// <summary>Immutable, portable lookup for one selected modern Unihan reading field.</summary>
public sealed class UnihanReadingDictionary
{
    private readonly Dictionary<int, IReadOnlyList<string>> _readings = new();

    private UnihanReadingDictionary(UnihanReadingOption option) => Option = option;

    public UnihanReadingOption Option { get; }

    public IReadOnlyList<string> GetReadings(Rune rune) =>
        _readings.TryGetValue(rune.Value, out var readings) ? readings : Array.Empty<string>();

    public static async Task<UnihanReadingDictionary> LoadAsync(Stream stream, string readingId)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var result = new UnihanReadingDictionary(UnihanReadingOption.Get(readingId));
        var field = result.Option.ReadingSystem.SourceField;
        var fieldName = field.ToString();
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var row = line.AsSpan();
            var firstTab = row.IndexOf('\t');
            var secondTab = firstTab < 0 ? -1 : row[(firstTab + 1)..].IndexOf('\t') + firstTab + 1;
            if (firstTab < 3 || secondTab <= firstTab || !row.StartsWith("U+", StringComparison.Ordinal)
                || row[(secondTab + 1)..].Contains('\t'))
                throw new FormatException("Expected U+codepoint<TAB>field<TAB>readings Unihan rows.");
            if (!row[(firstTab + 1)..secondTab].SequenceEqual(fieldName.AsSpan())) continue;

            var codepointText = row[2..firstTab];
            if (!int.TryParse(codepointText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codepoint)
                || !Rune.IsValid(codepoint))
                throw new FormatException("Invalid Unihan codepoint.");

            var candidates = ParseCandidates(row[(secondTab + 1)..].ToString(), result.Option);
            if (candidates.Count == 0) continue;
            if (result._readings.TryGetValue(codepoint, out var existing))
                candidates = Array.AsReadOnly(existing.Concat(candidates).Distinct(StringComparer.Ordinal).ToArray());
            result._readings[codepoint] = candidates;
        }
        if (result._readings.Count == 0)
            throw new FormatException($"No readings found for '{readingId}'.");
        return result;
    }

    private static IReadOnlyList<string> ParseCandidates(string value, UnihanReadingOption option)
    {
        var result = new List<string>();
        foreach (var item in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var reading = item;
            switch (option.ReadingSystem.SourceField)
            {
                case UnihanField.kHangul:
                    reading = reading.Split(':')[0];
                    break;
                case UnihanField.kHanyuPinlu:
                    reading = reading.Split('(')[0];
                    break;
                case UnihanField.kHanyuPinyin:
                case UnihanField.kTGHZ2013:
                case UnihanField.kXHC1983:
                    var colon = reading.IndexOf(':');
                    if (colon < 0) continue;
                    reading = reading[(colon + 1)..];
                    break;
                case UnihanField.kSMSZD2003Readings:
                    var marker = reading.IndexOf('粵');
                    if (option.Id.EndsWith("-Cantonese", StringComparison.Ordinal))
                    {
                        if (marker < 0) continue;
                        reading = reading[(marker + 1)..];
                    }
                    else
                    {
                        reading = marker < 0 ? reading : reading[..marker];
                    }
                    break;
            }
            foreach (var candidate in reading.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = candidate.TrimEnd('*');
                if (normalized.Length > 0 && !result.Contains(normalized, StringComparer.Ordinal))
                    result.Add(normalized);
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
