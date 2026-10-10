using System.Buffers;

namespace Minos.Transport;

/// <summary>
/// A UTF-8 JSON body in a buffer rented from an <see cref="ArrayPool{T}"/>: a request body written by
/// <see cref="Protocols.SystemOneRequestWriter"/>, or a response body read by <see cref="DecisionRawSerializer"/>.
/// </summary>
/// <remarks>
/// It is also the <see cref="IBufferWriter{T}"/> that fills it, growing by renting a larger buffer, copying and
/// returning the old one. <see cref="Dispose"/> clears the written bytes and returns the buffer; the content must not
/// be read after that. Not thread-safe; one owner at a time.
/// </remarks>
internal sealed class RawJson : IBufferWriter<byte>, IDisposable
{
    private const int MinimumGrowth = 256;

    private readonly ArrayPool<byte> _pool;
    private byte[]? _buffer;

    private RawJson(ArrayPool<byte> pool, byte[] buffer)
    {
        _pool = pool;
        _buffer = buffer;
    }

    /// <summary>Gets the number of bytes written.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the written bytes.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned.</exception>
    public ReadOnlySpan<byte> Span => Buffer.AsSpan(0, Length);

    /// <summary>Gets the written bytes, for an asynchronous write.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned.</exception>
    public ReadOnlyMemory<byte> Memory => Buffer.AsMemory(0, Length);

    private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(RawJson));

    /// <summary>Rents an empty body with room for at least <paramref name="initialCapacity"/> bytes.</summary>
    /// <param name="pool">The pool the buffers come from and go back to.</param>
    /// <param name="initialCapacity">The expected size.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson Create(ArrayPool<byte> pool, int initialCapacity)
        => new(pool, pool.Rent(Math.Max(initialCapacity, MinimumGrowth)));

    /// <inheritdoc />
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Buffer.Length - Length)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        Length += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Reserve(sizeHint);
        return Buffer.AsMemory(Length);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Reserve(sizeHint);
        return Buffer.AsSpan(Length);
    }

    /// <summary>Clears the written bytes and returns the buffer to the pool. Later calls do nothing.</summary>
    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null)
        {
            return;
        }

        _buffer = null;
        buffer.AsSpan(0, Length).Clear();
        _pool.Return(buffer);
    }

    // Makes room for at least sizeHint bytes after Length, or one byte when sizeHint is 0, growing the buffer if needed.
    private void Reserve(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        var buffer = Buffer;
        var needed = Math.Max(sizeHint, 1);
        if (buffer.Length - Length >= needed)
        {
            return;
        }

        var required = (long)Length + Math.Max(needed, MinimumGrowth);
        var doubled = (long)buffer.Length * 2;
        var size = Math.Min(Math.Max(required, doubled), Array.MaxLength);
        if (size < (long)Length + needed)
        {
            throw new InvalidOperationException("The JSON body is larger than the largest possible buffer.");
        }

        var larger = _pool.Rent((int)size);
        buffer.AsSpan(0, Length).CopyTo(larger);
        buffer.AsSpan(0, Length).Clear();
        _buffer = larger;
        _pool.Return(buffer);
    }
}
