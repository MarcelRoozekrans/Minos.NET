using System.Globalization;
using System.Text;
using System.Text.Json;

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

    /// <summary>Throws unless <paramref name="reader"/> is positioned on the start of an array.</summary>
    public static void EnsureStartArray(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected a JSON array but found {reader.TokenType.ToString()}.");
        }
    }

    /// <summary>Throws unless <paramref name="reader"/> is positioned on the start of an object.</summary>
    public static void EnsureStartObject(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a JSON object but found {reader.TokenType.ToString()}.");
        }
    }

    /// <summary>Advances to the next element of the current array; false at the array's end.</summary>
    public static bool NextElement(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
        {
            throw new JsonException("The JSON ends inside an array.");
        }

        return reader.TokenType != JsonTokenType.EndArray;
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

    /// <summary>
    /// Scans an answer object for its <c>name</c> and <c>type</c>, in either order, without allocating. The reader is a
    /// copy, positioned on the answer's start, so the caller's reader stays there.
    /// </summary>
    /// <param name="probe">A copy of the reader, on the answer's start.</param>
    /// <param name="keys">The question keys, from the definition.</param>
    /// <returns>The question's index, or -1 for a name the set does not have, and the answer's type.</returns>
    public static (int Index, AnswerType Type) Probe(Utf8JsonReader probe, byte[][] keys)
    {
        EnsureStartObject(ref probe);
        int? index = null;
        AnswerType? type = null;
        while (NextProperty(ref probe))
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

        return (index ?? throw MissingField("name"), type ?? throw MissingField("type"));
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
        EnsureStartObject(ref reader);
        double? probability = null;
        while (NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("probability"u8))
            {
                probability = ReadNumber(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        return probability ?? throw MissingField("probability");
    }

    /// <summary>Reads a choice answer's option index and confidence, writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    public static (int Choice, double Confidence) ReadChoice(ref Utf8JsonReader reader, byte[][] optionKeys, double[] buffer, int offset)
    {
        EnsureStartObject(ref reader);
        var hasProbabilities = false;
        var choice = -1;
        double? confidence = null;
        while (NextProperty(ref reader))
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
                confidence = ReadNumber(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        Require(choice >= 0, "choice");
        Require(hasProbabilities, "probabilities");
        return (choice, confidence ?? throw MissingField("confidence"));
    }

    /// <summary>
    /// Reads a score answer's most probable level, the lower one on a tie, its expected level and its confidence,
    /// writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.
    /// </summary>
    public static (int Level, double Expected, double Confidence) ReadScore(ref Utf8JsonReader reader, int levels, double[] buffer, int offset)
    {
        EnsureStartObject(ref reader);
        var hasProbabilities = false;
        double? expected = null;
        double? confidence = null;
        while (NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("score"u8))
            {
                expected = ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("probabilities"u8))
            {
                ReadScoreProbabilities(ref reader, levels, buffer, offset);
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

        Require(hasProbabilities, "probabilities");

        var best = 0;
        for (var i = 1; i < levels; i++)
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
        EnsureStartArray(ref reader);
        Array.Clear(buffer, offset, optionKeys.Length);
        while (NextElement(ref reader))
        {
            EnsureStartObject(ref reader);
            var index = -1;
            double? probability = null;
            while (NextProperty(ref reader))
            {
                if (reader.ValueTextEquals("value"u8))
                {
                    reader.Read();
                    index = ReadOptionIndex(ref reader, optionKeys);
                }
                else if (reader.ValueTextEquals("probability"u8))
                {
                    probability = ReadNumber(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            Require(index >= 0, "value");
            buffer[offset + index] = probability ?? throw MissingField("probability");
        }
    }

    // [{"value":level index,"label":…,"probability":p}, …]; the label is not read, the index decides.
    private static void ReadScoreProbabilities(ref Utf8JsonReader reader, int levels, double[] buffer, int offset)
    {
        reader.Read();
        EnsureStartArray(ref reader);
        Array.Clear(buffer, offset, levels);
        while (NextElement(ref reader))
        {
            EnsureStartObject(ref reader);
            var index = -1;
            double? probability = null;
            while (NextProperty(ref reader))
            {
                if (reader.ValueTextEquals("value"u8))
                {
                    reader.Read();
                    index = ReadLevelIndex(ref reader, levels);
                }
                else if (reader.ValueTextEquals("probability"u8))
                {
                    probability = ReadNumber(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            Require(index >= 0, "value");
            buffer[offset + index] = probability ?? throw MissingField("probability");
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

    private static double ReadNumber(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value))
        {
            throw new JsonException($"Expected a number but found {reader.TokenType.ToString()}.");
        }

        return value;
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

    private static void Require(bool present, string field)
    {
        if (!present)
        {
            throw MissingField(field);
        }
    }

    private static JsonException MissingField(string field) => new($"The answer has no '{field}' field.");
}
