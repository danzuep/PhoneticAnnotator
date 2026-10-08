using Unihan.Models;
using Unihan.Services;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Loads selected reading properties from a caller-provided Unihan text stream.
/// </summary>
public static class UnihanLookupLoader
{
    private static readonly UnihanField[] DefaultFields =
    [
        UnihanField.kMandarin,
        UnihanField.kCantonese,
        UnihanField.kJapanese
    ];

    /// <summary>
    /// Parses a Unihan text stream into an indexed lookup.
    /// </summary>
    /// <param name="inputStream">A readable stream positioned at the start of Unihan text data.</param>
    /// <param name="fields">The reading properties to retain; defaults to Mandarin, Cantonese, and Japanese.</param>
    /// <param name="cancellationToken">A token used to cancel parsing.</param>
    /// <returns>The indexed lookup. The input stream remains open and is left at its end.</returns>
    /// <exception cref="ArgumentException">The stream cannot be read or no valid fields were selected.</exception>
    public static async Task<UnihanLookup> LoadAsync(
        Stream inputStream,
        IEnumerable<UnihanField>? fields = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        if (!inputStream.CanRead)
        {
            throw new ArgumentException("The Unihan stream must be readable.", nameof(inputStream));
        }

        var selectedFields = (fields ?? DefaultFields).Distinct().ToArray();
        if (selectedFields.Length == 0 || selectedFields.Contains(UnihanField.Unknown))
        {
            throw new ArgumentException("Select one or more concrete Unihan fields.", nameof(fields));
        }

        return await UnihanParserService
            .ParseAsync<UnihanLookup>(inputStream, selectedFields, cancellationToken)
            .ConfigureAwait(false);
    }
}