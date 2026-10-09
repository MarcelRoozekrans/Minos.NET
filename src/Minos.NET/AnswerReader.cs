using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace Minos;

/// <summary>Reads typed answers from the <c>answers</c> object of a <c>/v1/systemone</c> response.</summary>
/// <remarks>
/// Infrastructure for the code the <c>[Questions]</c> source generator emits; application code does not call it.
/// The reader must hold complete JSON. Answer fields may appear in any order and unknown fields are skipped. Missing or
/// malformed data throws <see cref="JsonException"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AnswerReader
{
    /// <summary>Throws unless <paramref name="reader"/> is positioned on the start of an object.</summary>
    /// <param name="reader">The reader.</param>
    /// <exception cref="JsonException">The current token is not <see cref="JsonTokenType.StartObject"/>.</exception>
    public static void EnsureStartObject(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a JSON object but found {reader.TokenType.ToString()}.");
        }
    }

    /// <summary>Advances to the next property of the current object.</summary>
    /// <param name="reader">A reader inside an object, positioned before a property name or the object's end.</param>
    /// <returns><see langword="true"/> when positioned on a property name; <see langword="false"/> at the object's end.</returns>
    /// <exception cref="JsonException">The JSON ends inside the object.</exception>
    public static bool NextProperty(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
        {
            throw new JsonException("The JSON ends inside an object.");
        }

        return reader.TokenType == JsonTokenType.PropertyName;
    }

    /// <summary>Reads a Noul answer.</summary>
    /// <param name="reader">A reader positioned on the answer's start. It is left on the answer's end.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="JsonException">The answer is malformed, is not a Noul answer, or lacks <c>noul</c>.</exception>
    public static Noul ReadNoul(ref Utf8JsonReader reader)
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
        return new Noul(probability ?? throw MissingField("noul"));
    }

    /// <summary>Reads a Choice answer, writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    /// <typeparam name="T">The enum whose members are the options.</typeparam>
    /// <param name="reader">A reader positioned on the answer's start. It is left on the answer's end.</param>
    /// <param name="options">The options.</param>
    /// <param name="buffer">The probability buffer shared by one parse.</param>
    /// <param name="offset">The position of this answer's slice in <paramref name="buffer"/>.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="JsonException">The answer is malformed, is not a Choice answer, names an unknown option, or lacks a required field.</exception>
    public static Choice<T> ReadChoice<T>(ref Utf8JsonReader reader, DecisionOptionSet<T> options, double[] buffer, int offset)
        where T : struct, Enum
    {
        var (choice, confidence) = ReadChoiceCore(ref reader, options, buffer, offset);
        return new Choice<T>(options[choice], confidence, new ProbabilityMap<T>(buffer, offset, options));
    }

    /// <summary>
    /// Reads a Score answer, writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.
    /// The answer must carry a <c>legend</c> object, as the API requires, though this path does not expose it.
    /// </summary>
    /// <typeparam name="T">The enum whose members are the levels.</typeparam>
    /// <param name="reader">A reader positioned on the answer's start. It is left on the answer's end.</param>
    /// <param name="options">The levels, keyed <c>"0"</c>, <c>"1"</c>, … in rubric order.</param>
    /// <param name="buffer">The probability buffer shared by one parse.</param>
    /// <param name="offset">The position of this answer's slice in <paramref name="buffer"/>.</param>
    /// <returns>The answer; its value is the most probable level, the lower one on a tie.</returns>
    /// <exception cref="JsonException">The answer is malformed, is not a Score answer, names an unknown level, or lacks a required field.</exception>
    public static Score<T> ReadScore<T>(ref Utf8JsonReader reader, DecisionOptionSet<T> options, double[] buffer, int offset)
        where T : struct, Enum
    {
        var (level, expected, confidence) = ReadScoreCore(ref reader, options, buffer, offset);
        return new Score<T>(options[level], expected, confidence, new ProbabilityMap<T>(buffer, offset, options));
    }

    /// <summary>Reads a Choice answer's option index and confidence, writing its probabilities into <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    /// <param name="reader">A reader positioned on the answer's start. It is left on the answer's end.</param>
    /// <param name="options">The options.</param>
    /// <param name="buffer">The probability buffer shared by one parse.</param>
    /// <param name="offset">The position of this answer's slice in <paramref name="buffer"/>.</param>
    /// <returns>The chosen option's position and the confidence.</returns>
    internal static (int Choice, double Confidence) ReadChoiceCore(ref Utf8JsonReader reader, IDecisionOptionKeys options, double[] buffer, int offset)
    {
        ValidateSlice(options, buffer, offset);
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
                choice = ReadOptionIndex(ref reader, options, JsonTokenType.String);
            }
            else if (reader.ValueTextEquals("probabilities"u8))
            {
                ReadProbabilities(ref reader, options, buffer, offset);
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
    /// <param name="reader">A reader positioned on the answer's start. It is left on the answer's end.</param>
    /// <param name="options">The levels, keyed <c>"0"</c>, <c>"1"</c>, … in rubric order.</param>
    /// <param name="buffer">The probability buffer shared by one parse.</param>
    /// <param name="offset">The position of this answer's slice in <paramref name="buffer"/>.</param>
    /// <returns>The most probable level's position, the expected level and the confidence.</returns>
    internal static (int Level, double Expected, double Confidence) ReadScoreCore(
        ref Utf8JsonReader reader, IDecisionOptionKeys options, double[] buffer, int offset)
    {
        ValidateSlice(options, buffer, offset);
        if (options.Count == 0)
        {
            throw new ArgumentException("A score needs at least one level.", nameof(options));
        }

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
                // Required by the API but not exposed by the pooled path: check its shape and skip it.
                reader.Read();
                EnsureStartObject(ref reader);
                reader.Skip();
                hasLegend = true;
            }
            else if (reader.ValueTextEquals("probabilities"u8))
            {
                ReadProbabilities(ref reader, options, buffer, offset);
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
        for (var i = 1; i < options.Count; i++)
        {
            if (buffer[offset + i] > buffer[offset + best])
            {
                best = i;
            }
        }

        return (best, expected ?? throw MissingField("score"), confidence ?? throw MissingField("confidence"));
    }

    /// <summary>Creates the exception thrown when the response has no answer for a declared question.</summary>
    /// <param name="key">The question's wire key.</param>
    /// <returns>The exception.</returns>
    public static JsonException MissingAnswer(string key) => new($"The response has no answer for '{key}'.");

    private static void ValidateSlice(IDecisionOptionKeys options, double[] buffer, int offset)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, buffer.Length - options.Count);
    }

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

    private static int ReadOptionIndex(ref Utf8JsonReader reader, IDecisionOptionKeys options, JsonTokenType expected)
    {
        if (reader.TokenType != expected)
        {
            throw new JsonException($"Expected {expected.ToString()} but found {reader.TokenType.ToString()}.");
        }

        var index = options.IndexOfKey(ref reader);
        if (index < 0)
        {
            throw new JsonException($"'{reader.GetString()}' is not one of the options.");
        }

        return index;
    }

    private static void ReadProbabilities(ref Utf8JsonReader reader, IDecisionOptionKeys options, double[] buffer, int offset)
    {
        reader.Read();
        EnsureStartObject(ref reader);
        Array.Clear(buffer, offset, options.Count);

        while (NextProperty(ref reader))
        {
            var index = ReadOptionIndex(ref reader, options, JsonTokenType.PropertyName);
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
