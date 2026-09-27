using System.Text;
using ZeroAlloc.Jev.Transport;

namespace ZeroAlloc.Jev.Tests;

public sealed class JevRawSerializerTests
{
    [Fact]
    public void ContentType_IsJson()
        => Assert.Equal("application/json", new JevRawSerializer().ContentType);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(100_000)]
    public async Task Deserialize_ReadsTheWholeStream_AndDisposeReturnsTheBuffer(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)('a' + (i % 26));
        }

        var pool = new CountingPool();
        var serializer = new JevRawSerializer(pool);
        using var stream = new TrickleStream(bytes, chunk: 1000);

        var raw = await serializer.DeserializeAsync<RawJson>(stream);

        Assert.NotNull(raw);
        Assert.Equal(length, raw.Length);
        Assert.True(raw.Span.SequenceEqual(bytes));
        Assert.Equal(1, pool.Outstanding);
        raw.Dispose();
        raw.Dispose();
        Assert.Equal(0, pool.Outstanding);
        Assert.Throws<ObjectDisposedException>(() => raw.Span.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5000)]
    [InlineData(100_000)]
    public async Task Deserialize_SeekableStream_IsReadWithoutRegrowth(int length)
    {
        var bytes = new byte[length + 7];
        bytes.AsSpan().Fill((byte)'x');
        var pool = new CountingPool();
        var serializer = new JevRawSerializer(pool);
        using var stream = new MemoryStream(bytes);
        stream.Position = 7;

        using var raw = await serializer.DeserializeAsync<RawJson>(stream);

        Assert.Equal(length, raw!.Length);
        Assert.Equal(1, pool.Rented);
    }

    [Fact]
    public async Task Deserialize_OtherTypes_AreNotSupported()
    {
        var serializer = new JevRawSerializer();
        using var stream = new MemoryStream("{}"u8.ToArray());

        await Assert.ThrowsAsync<NotSupportedException>(async () => await serializer.DeserializeAsync<SystemOneResponse>(stream));
    }

    [Fact]
    public async Task Deserialize_ReturnsTheBuffer_WhenTheReadFails()
    {
        var pool = new CountingPool();
        var serializer = new JevRawSerializer(pool);
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellingStream(Encoding.UTF8.GetBytes("{\"answers\":"), cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await serializer.DeserializeAsync<RawJson>(stream, cancellation.Token));

        Assert.True(pool.Rented > 0);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task Serialize_WritesTheRawBytes()
    {
        var pool = new CountingPool();
        var serializer = new JevRawSerializer(pool);
        using var raw = RawJson.Create(pool, 4);
        "{\"a\":[1,2,3]}"u8.CopyTo(raw.GetSpan(13));
        raw.Advance(13);
        using var stream = new MemoryStream();

        await serializer.SerializeAsync(stream, raw);

        Assert.Equal("{\"a\":[1,2,3]}"u8.ToArray(), stream.ToArray());
    }

    [Fact]
    public async Task Serialize_OtherTypes_AreNotSupported()
    {
        var serializer = new JevRawSerializer();
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<NotSupportedException>(async () => await serializer.SerializeAsync(stream, "text"));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await serializer.SerializeAsync<RawJson?>(stream, null));
    }

    /// <summary>A stream that returns at most <c>chunk</c> bytes per read.</summary>
    private sealed class TrickleStream(byte[] bytes, int chunk) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
    }

    /// <summary>A stream that returns some bytes, then cancels the caller's token and fails the next read.</summary>
    private sealed class CancellingStream(byte[] prefix, CancellationTokenSource cancellation) : Stream
    {
        private bool _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (!_sent)
            {
                _sent = true;
                prefix.CopyTo(buffer);
                return prefix.Length;
            }

            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
