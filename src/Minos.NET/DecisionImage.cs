using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace Minos;

/// <summary>
/// An image a decision is about, sent with the state where the provider supports images: inline bytes and their media
/// type. Providers accept PNG, JPEG, WebP and GIF, as base64 data URLs; remote URLs and file ids are not supported.
/// </summary>
public readonly struct DecisionImage : IEquatable<DecisionImage>
{
    private static readonly string[] SupportedTypes = ["image/png", "image/jpeg", "image/webp", "image/gif"];

    private readonly string? _mediaType;
    private readonly byte[]? _data;

    private DecisionImage(string mediaType, byte[] data)
    {
        _mediaType = mediaType;
        _data = data;
    }

    /// <summary>
    /// Gets the media type, such as <c>image/png</c>. An uninitialized <see cref="DecisionImage"/>, the
    /// <see langword="default"/> value, has an empty media type and no data; <see cref="DecisionRequest.Images"/> rejects it.
    /// </summary>
    public string MediaType => _mediaType ?? string.Empty;

    /// <summary>Gets the image bytes; empty for the <see langword="default"/> value.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    internal bool IsInitialized => _data is not null;

    // "data:" + type + ";base64," + the padded base64 text: exactly the bytes of the URL.
    internal int DataUrlLength => 5 + MediaType.Length + 8 + Base64.GetMaxEncodedToUtf8Length(_data!.Length);

    /// <summary>Creates an image from its bytes. The bytes are copied.</summary>
    /// <param name="data">The image bytes.</param>
    /// <param name="mediaType"><c>image/png</c>, <c>image/jpeg</c>, <c>image/webp</c> or <c>image/gif</c>.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mediaType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or <paramref name="mediaType"/> is not supported.</exception>
    public static DecisionImage FromBytes(ReadOnlyMemory<byte> data, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(mediaType);
        var type = Supported(mediaType) ?? throw new ArgumentException(
            "The media type must be image/png, image/jpeg, image/webp or image/gif.", nameof(mediaType));
        if (data.IsEmpty)
        {
            throw new ArgumentException("The image has no data.", nameof(data));
        }

        return new DecisionImage(type, data.ToArray());
    }

    /// <summary>Creates an image from a <c>data:&lt;type&gt;;base64,&lt;data&gt;</c> URL.</summary>
    /// <param name="dataUrl">The data URL.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dataUrl"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="dataUrl"/> is not a base64 data URL of a supported type, or its data is empty or not base64.
    /// </exception>
    public static DecisionImage FromDataUrl(string dataUrl)
    {
        ArgumentNullException.ThrowIfNull(dataUrl);
        const string Prefix = "data:";
        const string Marker = ";base64,";
        var markerAt = dataUrl.IndexOf(Marker, StringComparison.Ordinal);
        if (!dataUrl.StartsWith(Prefix, StringComparison.Ordinal) || markerAt < 0)
        {
            throw new ArgumentException("The value must be a data:<type>;base64,<data> URL.", nameof(dataUrl));
        }

        var type = Supported(dataUrl[Prefix.Length..markerAt]) ?? throw new ArgumentException(
            "The media type must be image/png, image/jpeg, image/webp or image/gif.", nameof(dataUrl));
        byte[] data;
        try
        {
            data = Convert.FromBase64String(dataUrl[(markerAt + Marker.Length)..]);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The data URL's data is not base64.", nameof(dataUrl), exception);
        }

        if (data.Length == 0)
        {
            throw new ArgumentException("The image has no data.", nameof(dataUrl));
        }

        return new DecisionImage(type, data);
    }

    /// <summary>Compares two images for equality.</summary>
    /// <param name="left">The first image.</param>
    /// <param name="right">The second image.</param>
    /// <returns><see langword="true"/> when both have the same type and bytes.</returns>
    public static bool operator ==(DecisionImage left, DecisionImage right) => left.Equals(right);

    /// <summary>Compares two images for inequality.</summary>
    /// <param name="left">The first image.</param>
    /// <param name="right">The second image.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(DecisionImage left, DecisionImage right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(DecisionImage other)
        => string.Equals(MediaType, other.MediaType, StringComparison.Ordinal) && Data.Span.SequenceEqual(other.Data.Span);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DecisionImage other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(MediaType, _data?.Length ?? 0);

    /// <summary>Writes the image as a data URL, one JSON string value, base64-encoding into a pooled buffer.</summary>
    /// <remarks>
    /// The text is the media type, which is one of the supported ASCII types, and base64, so it needs no JSON escaping.
    /// It is written raw because the writer's default encoder would escape the plus sign.
    /// </remarks>
    internal void WriteDataUrl(Utf8JsonWriter writer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(DataUrlLength + 2);
        try
        {
            var buffer = rented.AsSpan();
            buffer[0] = (byte)'"';
            var written = 1;
            written += Encoding.ASCII.GetBytes("data:", buffer[written..]);
            written += Encoding.ASCII.GetBytes(MediaType, buffer[written..]);
            written += Encoding.ASCII.GetBytes(";base64,", buffer[written..]);
            Base64.EncodeToUtf8(_data, buffer[written..], out _, out var encoded);
            written += encoded;
            buffer[written++] = (byte)'"';
            writer.WriteRawValue(buffer[..written], skipInputValidation: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string? Supported(string mediaType)
    {
        foreach (var type in SupportedTypes)
        {
            if (string.Equals(type, mediaType, StringComparison.Ordinal))
            {
                return type;
            }
        }

        return null;
    }
}
