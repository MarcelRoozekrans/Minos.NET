using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jev.Net.Serialization;

namespace Jev.Net;

/// <summary>
/// A value the Jev API accepts as either plain text or structured JSON (an object or array),
/// such as a request <c>state</c>, question <c>instructions</c>, or a criteria description.
/// </summary>
[JsonConverter(typeof(JevContentConverter))]
public readonly struct JevContent : IEquatable<JevContent>
{
    private readonly string? _text;
    private readonly JsonElement _json;

    private JevContent(string text)
    {
        _text = text;
        _json = default;
    }

    private JevContent(JsonElement json)
    {
        _text = null;
        _json = json;
    }

    /// <summary>Gets a value indicating whether this content is plain text.</summary>
    public bool IsString => _text is not null;

    /// <summary>Creates text content.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static JevContent FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new JevContent(text);
    }

    /// <summary>
    /// Creates content from JSON. A JSON string becomes text; any other value is stored as a detached copy.
    /// </summary>
    /// <param name="json">The JSON value.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="json"/> is not a string, object or array.
    /// </exception>
    public static JevContent FromJson(JsonElement json)
    {
        return json.ValueKind switch
        {
            JsonValueKind.String => new JevContent(json.GetString()!),
            JsonValueKind.Object or JsonValueKind.Array => new JevContent(json.Clone()),
            _ => throw new ArgumentException(
                "Jev content must be a string, object or array.", nameof(json)),
        };
    }

    /// <summary>Converts text to content.</summary>
    /// <param name="text">The text.</param>
    public static implicit operator JevContent(string text) => FromString(text);

    /// <summary>Compares two content values for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when both hold equal text or deeply equal JSON.</returns>
    public static bool operator ==(JevContent left, JevContent right) => left.Equals(right);

    /// <summary>Compares two content values for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(JevContent left, JevContent right) => !left.Equals(right);

    /// <summary>Gets the text, when this content is plain text.</summary>
    /// <param name="text">The text, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when this content is plain text.</returns>
    public bool TryGetString([NotNullWhen(true)] out string? text)
    {
        text = _text;
        return text is not null;
    }

    /// <summary>Gets the JSON value, when this content is structured JSON.</summary>
    /// <param name="json">The JSON value, or <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when this content is structured JSON.</returns>
    public bool TryGetJson(out JsonElement json)
    {
        json = _json;
        return _text is null && json.ValueKind != JsonValueKind.Undefined;
    }

    /// <inheritdoc />
    public bool Equals(JevContent other)
    {
        if (_text is not null || other._text is not null)
        {
            return string.Equals(_text, other._text, StringComparison.Ordinal);
        }

        var json = _json;
        var otherJson = other._json;
        if (json.ValueKind == JsonValueKind.Undefined || otherJson.ValueKind == JsonValueKind.Undefined)
        {
            return json.ValueKind == otherJson.ValueKind;
        }

        return JsonElement.DeepEquals(json, otherJson);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is JevContent other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (_text is not null)
        {
            return StringComparer.Ordinal.GetHashCode(_text);
        }

        var json = _json;
        return json.ValueKind.GetHashCode();
    }

    /// <summary>Returns the text, or the raw JSON for structured content.</summary>
    /// <returns>The text or raw JSON; empty for uninitialized content.</returns>
    public override string ToString()
    {
        if (_text is not null)
        {
            return _text;
        }

        var json = _json;
        return json.ValueKind == JsonValueKind.Undefined ? string.Empty : json.GetRawText();
    }
}
