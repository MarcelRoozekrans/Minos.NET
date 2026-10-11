using System.Text;
using System.Text.Json;
using Minos.Transport;

namespace Minos.SystemOne.Protocols;

/// <summary>Reads the answers of a <c>/v1/systemone</c> response into numbers.</summary>
internal static class SystemOneAnswers
{
    /// <summary>Reads a Noul answer's probability. The reader is left on the answer's end.</summary>
    public static double ReadNoul(ref Utf8JsonReader reader)
    {
        AnswerReading.EnsureStartObject(ref reader);
        var hasType = false;
        double? probability = null;

        while (AnswerReading.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("type"u8))
            {
                ReadType(ref reader, "noul"u8);
                hasType = true;
            }
            else if (reader.ValueTextEquals("noul"u8))
            {
                probability = AnswerReading.ReadNumber(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        AnswerReading.Require(hasType, "type");
        return probability ?? throw AnswerReading.MissingField("noul");
    }

    /// <summary>Reads a Choice answer's option index and confidence, writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    public static (int Choice, double Confidence) ReadChoice(ref Utf8JsonReader reader, byte[][] optionKeys, double[] buffer, int offset)
    {
        AnswerReading.EnsureStartObject(ref reader);
        var hasType = false;
        var hasProbabilities = false;
        var choice = -1;
        double? confidence = null;

        while (AnswerReading.NextProperty(ref reader))
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
                confidence = AnswerReading.ReadNumber(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        AnswerReading.Require(hasType, "type");
        AnswerReading.Require(choice >= 0, "choice");
        AnswerReading.Require(hasProbabilities, "probabilities");
        return (choice, confidence ?? throw AnswerReading.MissingField("confidence"));
    }

    /// <summary>
    /// Reads a Score answer's most probable level, the lower one on a tie, its expected level and its confidence,
    /// writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.
    /// The answer must carry a <c>legend</c> object, which is validated and skipped.
    /// </summary>
    public static (int Level, double Expected, double Confidence) ReadScore(ref Utf8JsonReader reader, byte[][] optionKeys, double[] buffer, int offset)
    {
        AnswerReading.EnsureStartObject(ref reader);
        var hasType = false;
        var hasLegend = false;
        var hasProbabilities = false;
        double? expected = null;
        double? confidence = null;

        while (AnswerReading.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("type"u8))
            {
                ReadType(ref reader, "score"u8);
                hasType = true;
            }
            else if (reader.ValueTextEquals("score"u8))
            {
                expected = AnswerReading.ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("legend"u8))
            {
                // Required by the API but not exposed: check its shape and skip it.
                reader.Read();
                AnswerReading.EnsureStartObject(ref reader);
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
                confidence = AnswerReading.ReadNumber(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        AnswerReading.Require(hasType, "type");
        AnswerReading.Require(hasLegend, "legend");
        AnswerReading.Require(hasProbabilities, "probabilities");

        var best = 0;
        for (var i = 1; i < optionKeys.Length; i++)
        {
            if (buffer[offset + i] > buffer[offset + best])
            {
                best = i;
            }
        }

        return (best, expected ?? throw AnswerReading.MissingField("score"), confidence ?? throw AnswerReading.MissingField("confidence"));
    }

    private static void ReadType(ref Utf8JsonReader reader, ReadOnlySpan<byte> expected)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.String || !reader.ValueTextEquals(expected))
        {
            throw new JsonException($"Expected an answer of type '{Encoding.UTF8.GetString(expected)}'.");
        }
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
        AnswerReading.EnsureStartObject(ref reader);
        Array.Clear(buffer, offset, optionKeys.Length);

        while (AnswerReading.NextProperty(ref reader))
        {
            var index = ReadOptionIndex(ref reader, optionKeys, JsonTokenType.PropertyName);
            buffer[offset + index] = AnswerReading.ReadNumber(ref reader);
        }
    }
}
