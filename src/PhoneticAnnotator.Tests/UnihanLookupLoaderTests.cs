using System.Text;
using PhoneticAnnotator.Core;
using Unihan.Models;

namespace PhoneticAnnotator.Tests;

public sealed class UnihanLookupLoaderTests
{
    [Fact]
    public async Task LoadAsync_FiltersFieldsAndLeavesInputOpen()
    {
        const string data = "U+4E00\tkMandarin\tyī\nU+4E00\tkCantonese\tjat1\nU+4E00\tkDefinition\tone\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var lookup = await UnihanLookupLoader.LoadAsync(
            stream,
            [UnihanField.kMandarin, UnihanField.kCantonese]);

        Assert.True(stream.CanRead);
        Assert.True(lookup.TryGetValue(0x4E00, out var fields));
        Assert.Equal(new[] { "yī" }, fields[UnihanField.kMandarin]);
        Assert.Equal(new[] { "jat1" }, fields[UnihanField.kCantonese]);
        Assert.False(fields.ContainsKey(UnihanField.kDefinition));
    }

    [Fact]
    public async Task LoadAsync_DefaultFieldsIncludeMandarinCantoneseAndJapanese()
    {
        const string data = "U+4E00\tkMandarin\tyī\nU+4E00\tkCantonese\tjat1\nU+4E00\tkJapanese\tイチ\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var lookup = await UnihanLookupLoader.LoadAsync(stream);

        Assert.True(lookup.TryGetValue(0x4E00, out var fields));
        Assert.Contains(UnihanField.kMandarin, fields.Keys);
        Assert.Contains(UnihanField.kCantonese, fields.Keys);
        Assert.Contains(UnihanField.kJapanese, fields.Keys);
    }

    [Fact]
    public async Task LoadAsync_SupportsNonSeekableInput()
    {
        using var stream = new NonSeekableReadStream(Encoding.UTF8.GetBytes("U+4E00\tkMandarin\tyī\n"));

        var lookup = await UnihanLookupLoader.LoadAsync(stream);

        Assert.True(stream.CanRead);
        Assert.True(lookup.ContainsKey(0x4E00));
    }

    [Fact]
    public async Task LoadAsync_AcceptsReviewRowsWithCharacterInFirstColumn()
    {
        const string data = "U+4E95 井\tkCantonese\tzeng2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var lookup = await UnihanLookupLoader.LoadAsync(stream, [UnihanField.kCantonese]);

        Assert.True(lookup.TryGetValue(0x4E95, out var fields));
        Assert.Equal(new[] { "zeng2" }, fields[UnihanField.kCantonese]);
    }

    [Fact]
    public async Task LoadAsync_WhenCancelled_ThrowsAndLeavesInputOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("U+4E00\tkMandarin\tyī\n"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => UnihanLookupLoader.LoadAsync(stream, cancellationToken: cancellation.Token));

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task LoadAsync_EmptyFieldSelectionThrows()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(
            () => UnihanLookupLoader.LoadAsync(stream, []));
    }

    private sealed class NonSeekableReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}