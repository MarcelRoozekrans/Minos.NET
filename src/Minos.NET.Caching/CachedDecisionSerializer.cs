using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;

namespace Minos.Caching;

/// <summary>
/// The distributed-cache form of a <see cref="CachedDecision"/>: compact JSON with a version field. A slot is
/// <c>[valueIndex, value, confidence, offset]</c>. No reflection, so it is trim- and AOT-safe.
/// </summary>
/// <remarks>
/// Reading never throws. An unknown version or bytes it cannot read give <see cref="CachedDecision.Unreadable"/>, which the
/// stage treats as a miss and overwrites on the next success, so a format change in a later version cannot break callers
/// sharing one distributed cache.
/// </remarks>
internal sealed class CachedDecisionSerializer : IHybridCacheSerializer<CachedDecision>
{
    public void Serialize(CachedDecision value, IBufferWriter<byte> target)
    {
        using var writer = new Utf8JsonWriter(target);
        writer.WriteStartObject();
        writer.WriteNumber("v"u8, CachedDecision.Version);
        WriteString(writer, "model"u8, value.Model);
        WriteNumber(writer, "input_tokens"u8, value.InputTokens);
        WriteNumber(writer, "output_tokens"u8, value.OutputTokens);
        if (value.Cost is { } cost)
        {
            writer.WriteNumber("cost"u8, cost);
        }

        WriteString(writer, "id"u8, value.Id);
        WriteString(writer, "provider"u8, value.Provider);
        writer.WriteStartArray("slots"u8);
        foreach (var slot in value.Slots)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(slot.ValueIndex);
            writer.WriteNumberValue(slot.Value);
            writer.WriteNumberValue(slot.Confidence);
            writer.WriteNumberValue(slot.Offset);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("probabilities"u8);
        foreach (var probability in value.Probabilities)
        {
            writer.WriteNumberValue(probability);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public CachedDecision Deserialize(ReadOnlySequence<byte> source)
    {
        try
        {
            return Read(source) ?? CachedDecision.Unreadable;
        }
        catch (JsonException)
        {
            // Malformed or truncated JSON.
            return CachedDecision.Unreadable;
        }
        catch (InvalidOperationException)
        {
            // A string that is not valid UTF-8, which GetString cannot transcode.
            return CachedDecision.Unreadable;
        }
    }

    // Null when the version is unknown or a field has the wrong shape. Every token's type is checked before it is read,
    // so a shape error stops the read rather than reading on.
    private static CachedDecision? Read(ReadOnlySequence<byte> source)
    {
        var reader = new Utf8JsonReader(source);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        var fields = default(Fields);
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (!ReadProperty(ref reader, ref fields))
            {
                return null;
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject
            || fields.Version != CachedDecision.Version
            || fields.Slots is null
            || fields.Probabilities is null)
        {
            return null;
        }

        return new CachedDecision(
            fields.Slots, fields.Probabilities, fields.Model, fields.InputTokens, fields.OutputTokens, fields.Cost, fields.Id, fields.Provider);
    }

    // Reads the value of the property the reader is on; false when it has the wrong shape.
    private static bool ReadProperty(ref Utf8JsonReader reader, ref Fields fields)
    {
        if (reader.ValueTextEquals("v"u8))
        {
            return TryReadOptionalInt(ref reader, out fields.Version);
        }

        if (reader.ValueTextEquals("model"u8))
        {
            return TryReadString(ref reader, out fields.Model);
        }

        if (reader.ValueTextEquals("input_tokens"u8))
        {
            return TryReadOptionalInt(ref reader, out fields.InputTokens);
        }

        if (reader.ValueTextEquals("output_tokens"u8))
        {
            return TryReadOptionalInt(ref reader, out fields.OutputTokens);
        }

        if (reader.ValueTextEquals("cost"u8))
        {
            return TryReadOptionalDouble(ref reader, out fields.Cost);
        }

        if (reader.ValueTextEquals("id"u8))
        {
            return TryReadString(ref reader, out fields.Id);
        }

        if (reader.ValueTextEquals("provider"u8))
        {
            return TryReadString(ref reader, out fields.Provider);
        }

        if (reader.ValueTextEquals("slots"u8))
        {
            fields.Slots = ReadSlots(ref reader);
            return fields.Slots is not null;
        }

        if (reader.ValueTextEquals("probabilities"u8))
        {
            fields.Probabilities = ReadNumbers(ref reader);
            return fields.Probabilities is not null;
        }

        // A property a later version added: skip its value.
        if (!reader.Read())
        {
            return false;
        }

        reader.Skip();
        return true;
    }

    // [[valueIndex, value, confidence, offset], ...]; null when the shape is wrong.
    private static AnswerSlot[]? ReadSlots(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return null;
        }

        var slots = new List<AnswerSlot>();
        while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
        {
            if (!TryReadInt(ref reader, out var valueIndex)
                || !TryReadDouble(ref reader, out var value)
                || !TryReadDouble(ref reader, out var confidence)
                || !TryReadInt(ref reader, out var offset)
                || !reader.Read()
                || reader.TokenType != JsonTokenType.EndArray)
            {
                return null;
            }

            slots.Add(new AnswerSlot(valueIndex, value, confidence, offset));
        }

        return reader.TokenType == JsonTokenType.EndArray ? [.. slots] : null;
    }

    // [number, ...]; null when the shape is wrong.
    private static double[]? ReadNumbers(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return null;
        }

        var numbers = new List<double>();
        while (reader.Read() && reader.TokenType == JsonTokenType.Number)
        {
            if (!reader.TryGetDouble(out var number))
            {
                return null;
            }

            numbers.Add(number);
        }

        return reader.TokenType == JsonTokenType.EndArray ? [.. numbers] : null;
    }

    private static bool TryReadInt(ref Utf8JsonReader reader, out int value)
    {
        value = 0;
        return reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out value);
    }

    private static bool TryReadOptionalInt(ref Utf8JsonReader reader, out int? value)
    {
        var read = TryReadInt(ref reader, out var number);
        value = read ? number : null;
        return read;
    }

    private static bool TryReadDouble(ref Utf8JsonReader reader, out double value)
    {
        value = 0;
        return reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out value);
    }

    private static bool TryReadOptionalDouble(ref Utf8JsonReader reader, out double? value)
    {
        var read = TryReadDouble(ref reader, out var number);
        value = read ? number : null;
        return read;
    }

    private static bool TryReadString(ref Utf8JsonReader reader, out string? value)
    {
        value = null;
        if (!reader.Read() || reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
        {
            return false;
        }

        value = reader.GetString();
        return true;
    }

    private static void WriteString(Utf8JsonWriter writer, ReadOnlySpan<byte> name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, ReadOnlySpan<byte> name, int? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
    }

    // The fields read so far. The version is null until the "v" property is read.
    private struct Fields
    {
        public int? Version;
        public string? Model;
        public int? InputTokens;
        public int? OutputTokens;
        public double? Cost;
        public string? Id;
        public string? Provider;
        public AnswerSlot[]? Slots;
        public double[]? Probabilities;
    }
}
