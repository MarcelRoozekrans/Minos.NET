using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.Transport;

/// <summary>
/// The lenient envelope reading every protocol's response reader shares: a leading BOM, optional string fields, the
/// token usage and the <see cref="DecisionErrorKind.InvalidResponse"/> failures.
/// </summary>
/// <remarks>
/// An optional field of the wrong type is treated as absent, never as an error. The first occurrence of a field decides
/// it, even when that occurrence has the wrong type.
/// </remarks>
internal static class ResponseReading
{
    // Utf8JsonReader treats a leading UTF-8 BOM as an invalid start of a value, while the raw path's stream-based
    // deserializer skips one; a body read here must match that tolerance.
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>Returns <paramref name="json"/> without a leading UTF-8 byte order mark.</summary>
    /// <param name="json">The body.</param>
    /// <returns>The body from its first JSON byte.</returns>
    public static ReadOnlySpan<byte> SkipUtf8Bom(ReadOnlySpan<byte> json)
        => json.StartsWith(Utf8Bom) ? json[Utf8Bom.Length..] : json;

    /// <summary>
    /// Reads a string value, returning <paramref name="cache"/>'s string when it equals the bytes read, so a value that
    /// repeats across responses is not allocated again. Any other token is skipped as absent.
    /// </summary>
    /// <param name="reader">A reader on the field's value.</param>
    /// <param name="cache">The last value read, shared by the process. A race only replaces it; any string returned equals the bytes read.</param>
    /// <returns>The string, or <see langword="null"/> for a value that is not one.</returns>
    public static string? CachedStringOrSkip(ref Utf8JsonReader reader, ref string? cache)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        var cached = Volatile.Read(ref cache);
        if (cached is not null && reader.ValueTextEquals(cached))
        {
            return cached;
        }

        var value = reader.GetString();
        Volatile.Write(ref cache, value);
        return value;
    }

    /// <summary>Reads a string value. Any other token is skipped as absent.</summary>
    /// <param name="reader">A reader on the field's value.</param>
    /// <returns>The string, or <see langword="null"/> for a value that is not one.</returns>
    public static string? StringOrSkip(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip();
        return null;
    }

    /// <summary>
    /// Reads a <c>usage</c> object's <c>input_tokens</c>, <c>output_tokens</c> and <c>cost</c>, each when it is a number of
    /// the right kind. A value that is not, or a <c>usage</c> that is not an object, is absent; a repeated field is skipped.
    /// </summary>
    /// <param name="reader">A reader on the usage value.</param>
    /// <param name="inputTokens">The input tokens.</param>
    /// <param name="outputTokens">The output tokens.</param>
    /// <param name="cost">The cost.</param>
    public static void ReadUsage(ref Utf8JsonReader reader, ref int? inputTokens, ref int? outputTokens, ref double? cost)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        bool inputSeen = false, outputSeen = false, costSeen = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var field = reader.ValueTextEquals("input_tokens"u8) ? 1 : reader.ValueTextEquals("output_tokens"u8) ? 2 : reader.ValueTextEquals("cost"u8) ? 3 : 0;
            reader.Read();
            switch (field)
            {
                case 1 when !inputSeen:
                    inputSeen = true;
                    inputTokens = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var input) ? input : null;
                    break;
                case 2 when !outputSeen:
                    outputSeen = true;
                    outputTokens = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var output) ? output : null;
                    break;
                case 3 when !costSeen:
                    costSeen = true;
                    cost = reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var value) ? value : null;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    /// <summary>Creates an <see cref="DecisionErrorKind.InvalidResponse"/> failure with status 200.</summary>
    /// <param name="message">What is wrong with the body.</param>
    /// <returns>The failure.</returns>
    public static Result<DecisionResponse, DecisionError> Invalid(string message)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.InvalidResponse, message) { StatusCode = 200 });

    /// <summary>Creates the <see cref="DecisionErrorKind.InvalidResponse"/> failure for a body whose answers could not be read.</summary>
    /// <param name="exception">Why the body could not be read.</param>
    /// <returns>The failure, with status 200 and the exception.</returns>
    public static Result<DecisionResponse, DecisionError> Unreadable(JsonException exception)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(
            DecisionErrorKind.InvalidResponse,
            "The response could not be read as the question set's answers: " + exception.Message)
        {
            StatusCode = 200,
            Exception = exception,
        });
}
