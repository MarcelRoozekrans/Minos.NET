using System.Text.Json;

namespace Minos.Telemetry;

/// <summary>
/// Reads one top-level field of a <c>/v1/systemone</c> response body for telemetry, without allocating except the model string. Only the
/// listening path calls these, after <c>TypedEvaluation.ParseResponse</c> has read the whole body, so
/// the body is complete, valid JSON.
/// </summary>
internal static class ResponseFields
{
    /// <summary>
    /// Reads the top-level <c>model</c>, or returns <see langword="null"/> when it is absent or not a string. Builds a new
    /// string on every call: nothing is cached, so telemetry off costs nothing, and only a listening call pays.
    /// </summary>
    public static string? Model(ReadOnlySpan<byte> response)
    {
        var reader = new Utf8JsonReader(TypedEvaluation.SkipUtf8Bom(response));
        return TryReadTopLevel(ref reader, "model"u8) && reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
    }

    /// <summary>Gets the <c>usage</c> object's integer <paramref name="name"/>, or <see langword="null"/> when absent or not an integer.</summary>
    public static int? UsageInt32(ReadOnlySpan<byte> response, ReadOnlySpan<byte> name)
    {
        var reader = new Utf8JsonReader(TypedEvaluation.SkipUtf8Bom(response));
        if (!TryReadTopLevel(ref reader, "usage"u8) || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var match = reader.ValueTextEquals(name);
            reader.Read();
            if (match)
            {
                return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) ? value : null;
            }

            reader.Skip();
        }

        return null;
    }

    /// <summary>
    /// Moves a reader at the start of the body onto the value of the top-level property <paramref name="name"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the body is not an object or has no such property.</returns>
    public static bool TryReadTopLevel(ref Utf8JsonReader reader, ReadOnlySpan<byte> name)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var match = reader.ValueTextEquals(name);
            reader.Read();
            if (match)
            {
                return true;
            }

            reader.Skip();
        }

        return false;
    }
}
