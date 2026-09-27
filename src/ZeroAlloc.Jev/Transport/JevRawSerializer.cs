using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Jev.Transport;

/// <summary>
/// The serializer for <see cref="IJevApi.EvaluateRawAsync"/>: it passes <see cref="RawJson"/> bodies through as bytes,
/// without parsing them or building an object model.
/// </summary>
internal sealed class JevRawSerializer : IRestSerializer
{
    private const string OnlyRawJson = "Supports only RawJson, which it copies as bytes; no reflection or dynamic code is used.";

    // Large enough for a typical /v1/systemone response, small enough to stay out of the large object heap.
    private const int InitialResponseCapacity = 4096;

    private readonly ArrayPool<byte> _pool;

    /// <summary>Initializes a new instance of the <see cref="JevRawSerializer"/> class over the shared pool.</summary>
    /// <remarks>Public and parameterless because ZeroAlloc.Rest's generated DI registration constructs it.</remarks>
    public JevRawSerializer()
        : this(ArrayPool<byte>.Shared)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JevRawSerializer"/> class.</summary>
    /// <param name="pool">The pool response buffers are rented from.</param>
    internal JevRawSerializer(ArrayPool<byte> pool) => _pool = pool;

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <summary>Reads the whole stream into a pooled <see cref="RawJson"/>, which the caller owns and must dispose.</summary>
    /// <typeparam name="T">Must be <see cref="RawJson"/>.</typeparam>
    /// <param name="stream">The response body.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The body.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not <see cref="RawJson"/>.</exception>
    [RequiresUnreferencedCode(OnlyRawJson)]
    [RequiresDynamicCode(OnlyRawJson)]
    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (typeof(T) != typeof(RawJson))
        {
            throw new NotSupportedException("JevRawSerializer reads only RawJson, not " + typeof(T).Name + ".");
        }

        var raw = RawJson.Create(_pool, InitialResponseCapacity);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(raw.GetMemory(), ct).ConfigureAwait(false)) > 0)
            {
                raw.Advance(read);
            }

            return (T)(object)raw;
        }
        catch
        {
            raw.Dispose();
            throw;
        }
    }

    /// <summary>Writes a <see cref="RawJson"/> body's bytes to the stream. The body is not disposed.</summary>
    /// <typeparam name="T">Must be <see cref="RawJson"/>.</typeparam>
    /// <param name="stream">The request body stream.</param>
    /// <param name="value">The body.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when the bytes are written.</returns>
    /// <exception cref="NotSupportedException"><paramref name="value"/> is not a <see cref="RawJson"/>.</exception>
    [RequiresUnreferencedCode(OnlyRawJson)]
    [RequiresDynamicCode(OnlyRawJson)]
    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (value is not RawJson raw)
        {
            throw new NotSupportedException("JevRawSerializer writes only RawJson, not " + typeof(T).Name + ".");
        }

        return stream.WriteAsync(raw.Memory, ct);
    }
}
