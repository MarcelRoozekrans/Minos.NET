using System.Globalization;
using System.Text;
using System.Text.Json;
using Minos.Transport;

namespace Minos.OpenAI.Protocols;

/// <summary>Reads the elements of a <c>/v1/decisions</c> <c>answers</c> array into numbers.</summary>
internal static class OpenAIAnswers
{
    /// <summary>An answer's <c>type</c>.</summary>
    public enum AnswerType
    {
        /// <summary>A type this reader does not know.</summary>
        Unknown,

        /// <summary><c>predicate</c>, a Noul's answer.</summary>
        Predicate,

        /// <summary><c>choice</c>, a Choice's answer.</summary>
        Choice,

        /// <summary><c>score</c>, a Score's answer.</summary>
        Score,

        /// <summary><c>refusal</c>: the model declined to answer.</summary>
        Refusal,
    }

    /// <summary>
    /// Scans an answer object for its <c>name</c> and <c>type</c>, in either order, without allocating. The reader is a
    /// copy, positioned on the answer's start, so the caller's reader stays there.
    /// </summary>
    /// <param name="probe">A copy of the reader, on the answer's start.</param>
    /// <param name="keys">The question keys, from the definition.</param>
    /// <returns>The question's index, or -1 for a name the set does not have, and the answer's type.</returns>
    public static (int Index, AnswerType Type) Probe(Utf8JsonReader probe, byte[][] keys)
    {
        AnswerReading.EnsureStartObject(ref probe);
        int? index = null;
        AnswerType? type = null;
        while (AnswerReading.NextProperty(ref probe))
        {
            if (probe.ValueTextEquals("name"u8))
            {
                probe.Read();
                if (probe.TokenType != JsonTokenType.String)
                {
                    throw new JsonException($"Expected the answer's 'name' to be a string but found {probe.TokenType.ToString()}.");
                }

                index = Utf8Keys.IndexOf(ref probe, keys);
            }
            else if (probe.ValueTextEquals("type"u8))
            {
                probe.Read();
                type = probe.TokenType != JsonTokenType.String ? AnswerType.Unknown
                    : probe.ValueTextEquals("predicate"u8) ? AnswerType.Predicate
                    : probe.ValueTextEquals("choice"u8) ? AnswerType.Choice
                    : probe.ValueTextEquals("score"u8) ? AnswerType.Score
                    : probe.ValueTextEquals("refusal"u8) ? AnswerType.Refusal
                    : AnswerType.Unknown;
                probe.Skip();
            }
            else
            {
                probe.Skip();
            }
        }

        return (index ?? throw AnswerReading.MissingField("name"), type ?? throw AnswerReading.MissingField("type"));
    }

    /// <summary>Throws unless an answer of <paramref name="type"/> answers a question of <paramref name="kind"/>.</summary>
    public static void EnsureType(AnswerType type, QuestionKind kind)
    {
        var expected = kind switch
        {
            QuestionKind.Noul => AnswerType.Predicate,
            QuestionKind.Choice => AnswerType.Choice,
            _ => AnswerType.Score,
        };

        if (type != expected)
        {
            throw new JsonException($"Expected an answer of type '{Encoding.UTF8.GetString(TypeName(expected))}'.");
        }
    }

    /// <summary>Reads a predicate answer's probability. The reader is left on the answer's end.</summary>
    public static double ReadPredicate(ref Utf8JsonReader reader)
    {
        AnswerReading.EnsureStartObject(ref reader);
        double? probability = null;
        while (AnswerReading.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("probability"u8))
            {
                probability = AnswerReading.ReadProbability(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        return probability ?? throw AnswerReading.MissingField("probability");
    }

    /// <summary>Reads a choice answer's option index and confidence, writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    public static (int Choice, double Confidence) ReadChoice(ref Utf8JsonReader reader, byte[][] optionKeys, double[] buffer, int offset)
    {
        AnswerReading.EnsureStartObject(ref reader);
        var hasProbabilities = false;
        var choice = -1;
        double? confidence = null;
        while (AnswerReading.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("choice"u8))
            {
                reader.Read();
                choice = ReadOptionIndex(ref reader, optionKeys);
            }
            else if (reader.ValueTextEquals("probabilities"u8))
            {
                ReadChoiceProbabilities(ref reader, optionKeys, buffer, offset);
                hasProbabilities = true;
            }
            else if (reader.ValueTextEquals("confidence"u8))
            {
                confidence = AnswerReading.ReadProbability(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        AnswerReading.Require(choice >= 0, "choice");
        AnswerReading.Require(hasProbabilities, "probabilities");
        return (choice, confidence ?? throw AnswerReading.MissingField("confidence"));
    }

    /// <summary>
    /// Reads a score answer's most probable level, the lower one on a tie, its expected level and its confidence,
    /// writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.
    /// </summary>
    public static (int Level, double Expected, double Confidence) ReadScore(ref Utf8JsonReader reader, int levels, double[] buffer, int offset)
    {
        AnswerReading.EnsureStartObject(ref reader);
        var hasProbabilities = false;
        double? expected = null;
        double? confidence = null;
        while (AnswerReading.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("score"u8))
            {
                expected = AnswerReading.ReadExpectedLevel(ref reader, levels);
            }
            else if (reader.ValueTextEquals("probabilities"u8))
            {
                ReadScoreProbabilities(ref reader, levels, buffer, offset);
                hasProbabilities = true;
            }
            else if (reader.ValueTextEquals("confidence"u8))
            {
                confidence = AnswerReading.ReadProbability(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        AnswerReading.Require(hasProbabilities, "probabilities");

        var best = 0;
        for (var i = 1; i < levels; i++)
        {
            if (buffer[offset + i] > buffer[offset + best])
            {
                best = i;
            }
        }

        return (best, expected ?? throw AnswerReading.MissingField("score"), confidence ?? throw AnswerReading.MissingField("confidence"));
    }

    private static ReadOnlySpan<byte> TypeName(AnswerType type) => type switch
    {
        AnswerType.Predicate => "predicate"u8,
        AnswerType.Choice => "choice"u8,
        _ => "score"u8,
    };

    // [{"value":key,"probability":p}, …]
    private static void ReadChoiceProbabilities(ref Utf8JsonReader reader, byte[][] optionKeys, double[] buffer, int offset)
    {
        reader.Read();
        AnswerReading.EnsureStartArray(ref reader);
        Array.Clear(buffer, offset, optionKeys.Length);
        while (AnswerReading.NextElement(ref reader))
        {
            AnswerReading.EnsureStartObject(ref reader);
            var index = -1;
            double? probability = null;
            while (AnswerReading.NextProperty(ref reader))
            {
                if (reader.ValueTextEquals("value"u8))
                {
                    reader.Read();
                    index = ReadOptionIndex(ref reader, optionKeys);
                }
                else if (reader.ValueTextEquals("probability"u8))
                {
                    probability = AnswerReading.ReadProbability(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            AnswerReading.Require(index >= 0, "value");
            buffer[offset + index] = probability ?? throw AnswerReading.MissingField("probability");
        }
    }

    // [{"value":level index,"label":…,"probability":p}, …]; the label is not read, the index decides.
    private static void ReadScoreProbabilities(ref Utf8JsonReader reader, int levels, double[] buffer, int offset)
    {
        reader.Read();
        AnswerReading.EnsureStartArray(ref reader);
        Array.Clear(buffer, offset, levels);
        while (AnswerReading.NextElement(ref reader))
        {
            AnswerReading.EnsureStartObject(ref reader);
            var index = -1;
            double? probability = null;
            while (AnswerReading.NextProperty(ref reader))
            {
                if (reader.ValueTextEquals("value"u8))
                {
                    reader.Read();
                    index = ReadLevelIndex(ref reader, levels);
                }
                else if (reader.ValueTextEquals("probability"u8))
                {
                    probability = AnswerReading.ReadProbability(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            AnswerReading.Require(index >= 0, "value");
            buffer[offset + index] = probability ?? throw AnswerReading.MissingField("probability");
        }
    }

    private static int ReadLevelIndex(ref Utf8JsonReader reader, int levels)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var index))
        {
            throw new JsonException($"Expected a level index but found {reader.TokenType.ToString()}.");
        }

        if (index < 0 || index >= levels)
        {
            throw new JsonException($"'{index.ToString(CultureInfo.InvariantCulture)}' is not one of the levels.");
        }

        return index;
    }

    private static int ReadOptionIndex(ref Utf8JsonReader reader, byte[][] optionKeys)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected String but found {reader.TokenType.ToString()}.");
        }

        var index = Utf8Keys.IndexOf(ref reader, optionKeys);
        if (index < 0)
        {
            throw new JsonException($"'{reader.GetString()}' is not one of the options.");
        }

        return index;
    }
}
