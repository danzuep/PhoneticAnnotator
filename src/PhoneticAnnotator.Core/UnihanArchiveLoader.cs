using System.IO.Compression;
using Unihan.Models;

namespace PhoneticAnnotator.Core;

/// <summary>
/// Loads Unihan reading data from a Unicode Character Database archive.
/// </summary>
public static class UnihanArchiveLoader
{
    private const string ReadingsFileName = "Unihan_Readings.txt";

    /// <summary>
    /// Reads <c>Unihan_Readings.txt</c> from a caller-provided Unicode data archive.
    /// </summary>
    /// <param name="archiveStream">A readable, seekable stream containing a UCD ZIP archive.</param>
    /// <param name="fields">The reading properties to retain.</param>
    /// <param name="cancellationToken">A token used to cancel parsing.</param>
    /// <returns>The indexed lookup. The archive stream remains open.</returns>
    /// <exception cref="ArgumentException">The stream cannot be read or seeked.</exception>
    /// <exception cref="InvalidDataException">The archive does not contain a Unihan readings file.</exception>
    public static async Task<UnihanLookup> LoadAsync(
        Stream archiveStream,
        IEnumerable<UnihanField>? fields = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        if (!archiveStream.CanRead || !archiveStream.CanSeek)
        {
            throw new ArgumentException("The Unicode data archive must be readable and seekable.", nameof(archiveStream));
        }

        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        var readingsEntry = archive.GetEntry(ReadingsFileName)
            ?? archive.Entries.FirstOrDefault(entry =>
                entry.FullName.EndsWith($"/{ReadingsFileName}", StringComparison.Ordinal));
        if (readingsEntry is null)
        {
            throw new InvalidDataException($"The archive does not contain {ReadingsFileName}.");
        }

        await using var readingsStream = readingsEntry.Open();
        return await UnihanLookupLoader
            .LoadAsync(readingsStream, fields, cancellationToken)
            .ConfigureAwait(false);
    }
}