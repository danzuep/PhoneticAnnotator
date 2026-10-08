using System.Text;
using Unihan.Models;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Reads character-level candidates from an already indexed Unihan lookup.
/// </summary>
public sealed class UnihanCharacterReadingProvider(UnihanLookup lookup) : ICharacterReadingProvider
{
    private static readonly IReadOnlyList<string> NoReadings = Array.Empty<string>();

    private readonly UnihanLookup _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));

    /// <inheritdoc />
    public IReadOnlyList<string> GetReadings(Rune rune, UnihanField field)
    {
        if (!_lookup.TryGetValue(rune.Value, out var fieldLookup)
            || !fieldLookup.TryGetValue(field, out var fieldValues))
        {
            return NoReadings;
        }

        var readings = fieldValues
            .SelectMany(static value => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        return readings.Length == 0 ? NoReadings : Array.AsReadOnly(readings);
    }
}