using System.Text;
using System.Text.Json;

namespace Minos.Protocols;

/// <summary>Reads the answers of a <c>/v1/systemone</c> response into numbers.</summary>
internal static class SystemOneAnswers
{
    /// <summary>Throws unless <paramref name="reader"/> is positioned on the start of an object.</summary>
    public static void EnsureStartObject(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a JSON object but found {reader.TokenType.ToString()}.");
        }
    }

    /// <summary>Advances to the next property of the current object; false at the object's end.</summary>
    public static bool NextProperty(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
        {
            throw new JsonException("The JSON ends inside an object.");
        }

        return reader.TokenType == JsonTokenType.PropertyName;
    }

    /// <summary>Reads a Noul answer's probability. The reader is left on the answer's end.</summary>
    public static double ReadNoul(ref Utf8JsonReader reader)
    {
        EnsureStartObject(ref reader);
        var hasType = false;
        double? probability = null;

        while (NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("type"u8))
            {
                ReadType(ref reader, "noul"u8);
                hasType = true;
            }
            else if (reader.ValueTextEquals("noul"u8))
            {
                probability = ReadNumber(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        Require(hasType, "type");
        return probability ?? throw MissingField("noul");
    }

    /// <summary>Reads a Choice answer's option index and confidence, writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    public static (int Choice, double Confidence) ReadChoice(ref Utf8JsonReader reader, byte[][] optionKeys, double[] buffer, int offset)
    {
        EnsureStartObject(ref reader);
        var hasType = false;
        var hasProbabilities = false;
        var choice = -1;
        double? confidence = null;

        while (NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("type"u8))
            {
                ReadType(ref reader, "choice"u8);
                hasType = true;
            }
            else if (reader.ValueTextEquals("choice"u8))
            {
                reader.Read();
                choice = ReadOptionIndex(ref reader, optionKeys, JsonTokenType.String);
            }
            else if (reader.ValueTextEquals("probabilities"u8))
            {
                ReadProbabilities(ref reader, optionKeys, buffer, offset);
                hasProbabilities = true;
            }
            else if (reader.ValueTextEquals("confidence"u8))
            {
                confidence = ReadNumber(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        Require(hasType, "type");
        Require(choice >= 0, "choice");
        Require(hasProbabilities, "probabilities");
        return (choice, confidence ?? throw MissingField("confidence"));
    }

    /// <summary>
    /// Reads a Score answer's most probable level, the lower one on a tie, its expected level and its confidence,
    /// writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.
    /// The answer must carry a <c>legend</c> object, which is validated and skipped.
    /// </summary>
    public static (int Level, double Expected, double Confidence) ReadScore(ref Utf8JsonReader reader, byte[][] optionKeys, double[] buffer, int offset)
    {
        EnsureStartObject(ref reader);
        var hasType = false;
        var hasLegend = false;
        var hasProbabilities = false;
        double? expected = null;
        double? confidence = null;

        while (NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("type"u8))
            {
                ReadType(ref reader, "score"u8);
                hasType = true;
            }
            else if (reader.ValueTextEquals("score"u8))
            {
                expected = ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("legend"u8))
            {
                // Required by the API but not exposed: check its shape and skip it.
                reader.Read();
                EnsureStartObject(ref reader);
                reader.Skip();
                hasLegend = true;
            }
            else if (reader.ValueTextEquals("probabilities"u8))
            {
                ReadProbabilities(ref reader, optionKeys, buffer, offset);
                hasProbabilities = true;
            }
            else if (reader.ValueTextEquals("confidence"u8))
            {
                confidence = ReadNumber(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        Require(hasType, "type");
        Require(hasLegend, "legend");
        Require(hasProbabilities, "probabilities");

        var best = 0;
        for (var i = 1; i < optionKeys.Length; i++)
        {
            if (buffer[offset + i] > buffer[offset + best])
            {
                best = i;
            }
        }

        return (best, expected ?? throw MissingField("score"), confidence ?? throw MissingField("confidence"));
    }

    /// <summary>Creates the exception thrown when the response has no answer for a declared question.</summary>
    public static JsonException MissingAnswer(string key) => new($"The response has no answer for '{key}'.");

    private static void ReadType(ref Utf8JsonReader reader, ReadOnlySpan<byte> expected)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.String || !reader.ValueTextEquals(expected))
        {
            throw new JsonException($"Expected an answer of type '{Encoding.UTF8.GetString(expected)}'.");
        }
    }

    private static double ReadNumber(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value))
        {
            throw new JsonException($"Expected a number but found {reader.TokenType.ToString()}.");
        }

        return value;
    }

    private static int ReadOptionIndex(ref Utf8JsonReader reader, byte[][] optionKeys, JsonTokenType expected)
    {
        if (reader.TokenType != expected)
        {
            throw new JsonException($"Expected {expected.ToString()} but found {reader.TokenType.ToString()}.");
        }

        var index = Utf8Keys.IndexOf(ref reader, optionKeys);
        if (index < 0)
        {
            throw new JsonException($"'{reader.GetString()}' is not one of the options.");
        }

        return index;
    }

    private static void ReadProbabilities(ref Utf8JsonReader reader, byte[][] optionKeys, double[] buffer, int offset)
    {
        reader.Read();
        EnsureStartObject(ref reader);
        Array.Clear(buffer, offset, optionKeys.Length);

        while (NextProperty(ref reader))
        {
            var index = ReadOptionIndex(ref reader, optionKeys, JsonTokenType.PropertyName);
            buffer[offset + index] = ReadNumber(ref reader);
        }
    }

    private static void Require(bool present, string field)
    {
        if (!present)
        {
            throw MissingField(field);
        }
    }

    private static JsonException MissingField(string field) => new($"The answer has no '{field}' field.");
}
