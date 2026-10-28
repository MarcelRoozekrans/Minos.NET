using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Minos.Transport;

/// <summary>The strict token reading every protocol's answer reader shares. Each failure is a <see cref="JsonException"/>.</summary>
internal static class AnswerReading
{
    /// <summary>Throws unless <paramref name="reader"/> is positioned on the start of an object.</summary>
    /// <param name="reader">The reader.</param>
    public static void EnsureStartObject(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a JSON object but found {reader.TokenType.ToString()}.");
        }
    }

    /// <summary>Throws unless <paramref name="reader"/> is positioned on the start of an array.</summary>
    /// <param name="reader">The reader.</param>
    public static void EnsureStartArray(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected a JSON array but found {reader.TokenType.ToString()}.");
        }
    }

    /// <summary>Advances to the next property of the current object; false at the object's end.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>Whether the reader is on a property name.</returns>
    public static bool NextProperty(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
        {
            throw new JsonException("The JSON ends inside an object.");
        }

        return reader.TokenType == JsonTokenType.PropertyName;
    }

    /// <summary>Advances to the next element of the current array; false at the array's end.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>Whether the reader is on an element.</returns>
    public static bool NextElement(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
        {
            throw new JsonException("The JSON ends inside an array.");
        }

        return reader.TokenType != JsonTokenType.EndArray;
    }

    /// <summary>Reads the value after a property name as a probability or a confidence: a number from 0 to 1.</summary>
    /// <param name="reader">A reader on a property name.</param>
    /// <returns>The number.</returns>
    public static double ReadProbability(ref Utf8JsonReader reader)
        => ReadInRange(ref reader, 1, expectedLevel: false);

    /// <summary>Reads the value after a property name as a Score's expected level: a number from 0 to the top level's index.</summary>
    /// <param name="reader">A reader on a property name.</param>
    /// <param name="levels">The number of levels.</param>
    /// <returns>The number.</returns>
    public static double ReadExpectedLevel(ref Utf8JsonReader reader, int levels)
        => ReadInRange(ref reader, levels - 1, expectedLevel: true);

    // A provider that answers outside the range breaks the protocol, so the value is rejected rather than clamped.
    // The message is built only on failure: reading an answer must not allocate.
    private static double ReadInRange(ref Utf8JsonReader reader, int max, bool expectedLevel)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException($"Expected a number but found {reader.TokenType.ToString()}.");
        }

        if (!reader.TryGetDouble(out var value) || !(value >= 0 && value <= max))
        {
            var text = Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);
            var expected = expectedLevel
                ? "an expected level from 0 to " + max.ToString(CultureInfo.InvariantCulture)
                : "a probability from 0 to 1";
            throw new JsonException($"Expected {expected} but found {text}.");
        }

        return value;
    }

    /// <summary>Throws the <see cref="MissingField"/> exception unless <paramref name="present"/>.</summary>
    /// <param name="present">Whether the field was read.</param>
    /// <param name="field">The field's wire name.</param>
    public static void Require(bool present, string field)
    {
        if (!present)
        {
            throw MissingField(field);
        }
    }

    /// <summary>Creates the exception thrown when an answer lacks a required field.</summary>
    /// <param name="field">The field's wire name.</param>
    /// <returns>The exception.</returns>
    public static JsonException MissingField(string field) => new($"The answer has no '{field}' field.");

    /// <summary>Creates the exception thrown when the response has no answer for a declared question.</summary>
    /// <param name="key">The question's key.</param>
    /// <returns>The exception.</returns>
    public static JsonException MissingAnswer(string key) => new($"The response has no answer for '{key}'.");
}
