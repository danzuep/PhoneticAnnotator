using System.IO.Compression;
using System.Text;
using PhoneticAnnotator.Core;
using Unihan.Models;

namespace PhoneticAnnotator.Tests;

public sealed class UnihanArchiveLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReadsReadingsFileAndLeavesArchiveOpen()
    {
        using var archive = CreateArchive(
            "nested/Unihan_Readings.txt",
            "U+4E00\tkMandarin\tyī\nU+4E00\tkCantonese\tjat1\n");

        var lookup = await UnihanArchiveLoader.LoadAsync(
            archive,
            [UnihanField.kCantonese]);

        Assert.True(archive.CanRead);
        Assert.True(lookup.TryGetValue(0x4E00, out var fields));
        Assert.Equal(new[] { "jat1" }, fields[UnihanField.kCantonese]);
        Assert.False(fields.ContainsKey(UnihanField.kMandarin));
    }

    [Fact]
    public async Task LoadAsync_MissingReadingsFileThrowsInvalidDataException()
    {
        using var archive = CreateArchive("ReadMe.txt", "not unihan data");

        await Assert.ThrowsAsync<InvalidDataException>(() => UnihanArchiveLoader.LoadAsync(archive));
    }

    private static MemoryStream CreateArchive(string entryName, string contents)
    {
        var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open(), Encoding.UTF8))
        {
            writer.Write(contents);
        }

        archiveStream.Position = 0;
        return archiveStream;
    }
}