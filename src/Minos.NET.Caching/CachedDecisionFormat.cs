using System.Buffers;
using System.Text.Json;

namespace Minos.Caching;

/// <summary>
/// The distributed-cache form of a <see cref="CachedDecision"/>: compact JSON with a version field. A slot is
/// <c>[valueIndex, value, confidence, offset]</c>. An entry with an answer whose probabilities are not the model's own
/// is version 2 and adds <c>"sources"</c>, one <see cref="ProbabilitySource"/> number per slot. No reflection, so it is
/// trim- and AOT-safe.
/// </summary>
/// <remarks>
/// Reading never throws. An unknown version or bytes it cannot read give <see cref="CachedDecision.Unreadable"/>, which the
/// stage treats as a miss and overwrites on the next success, so a format change in a later version cannot break callers
/// sharing one distributed cache.
/// </remarks>
internal static class CachedDecisionFormat
{
    /// <summary>The bytes the distributed cache stores for <paramref name="value"/>.</summary>
    /// <param name="value">The entry.</param>
    /// <returns>Its compact JSON, UTF-8 encoded.</returns>
    public static byte[] Write(CachedDecision value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            var marked = HasMarkedSlot(value.Slots);
            writer.WriteNumber("v"u8, marked ? CachedDecision.CurrentVersion : CachedDecision.ModelOnlyVersion);
            WriteString(writer, "model"u8, value.Model);
            WriteNumber(writer, "input_tokens"u8, value.InputTokens);
            WriteNumber(writer, "output_tokens"u8, value.OutputTokens);
            if (value.Cost is { } cost)
            {
                writer.WriteNumber("cost"u8, cost);
            }

            WriteString(writer, "id"u8, value.Id);
            WriteString(writer, "provider"u8, value.UpstreamProvider);
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
            if (marked)
            {
                writer.WriteStartArray("sources"u8);
                foreach (var slot in value.Slots)
                {
                    writer.WriteNumberValue((int)slot.Source);
                }

                writer.WriteEndArray();
            }

            writer.WriteStartArray("probabilities"u8);
            foreach (var probability in value.Probabilities)
            {
                writer.WriteNumberValue(probability);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The entry <paramref name="source"/> holds; never throws.</summary>
    /// <param name="source">Bytes read from the distributed cache.</param>
    /// <returns>The entry, or <see cref="CachedDecision.Unreadable"/> for an unknown version or bytes that are no entry.</returns>
    public static CachedDecision Read(ReadOnlySpan<byte> source)
    {
        try
        {
            return TryRead(source) ?? CachedDecision.Unreadable;
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
    private static CachedDecision? TryRead(ReadOnlySpan<byte> source)
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
            || fields.Slots is null
            || fields.Probabilities is null
            || !TryApplySources(fields.Version, fields.Sources, fields.Slots))
        {
            return null;
        }

        return new CachedDecision(
            fields.Slots, fields.Probabilities, fields.Model, fields.InputTokens, fields.OutputTokens, fields.Cost, fields.Id, fields.UpstreamProvider);
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
            return TryReadString(ref reader, out fields.UpstreamProvider);
        }

        if (reader.ValueTextEquals("slots"u8))
        {
            fields.Slots = ReadSlots(ref reader);
            return fields.Slots is not null;
        }

        if (reader.ValueTextEquals("sources"u8))
        {
            fields.Sources = ReadSources(ref reader);
            return fields.Sources is not null;
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

    // [source, ...], each a ProbabilitySource number; null when the shape is wrong or a value is not a known source.
    private static ProbabilitySource[]? ReadSources(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return null;
        }

        var sources = new List<ProbabilitySource>();
        while (reader.Read() && reader.TokenType == JsonTokenType.Number)
        {
            if (!reader.TryGetInt32(out var number) || number is < (int)ProbabilitySource.Model or > (int)ProbabilitySource.Verbalized)
            {
                return null;
            }

            sources.Add((ProbabilitySource)number);
        }

        return reader.TokenType == JsonTokenType.EndArray ? [.. sources] : null;
    }

    // Checks the version against the sources and sets each slot's source. A version 1 entry has no sources, a version 2
    // entry has one per slot, and any other version is unknown.
    private static bool TryApplySources(int? version, ProbabilitySource[]? sources, AnswerSlot[] slots)
    {
        switch (version)
        {
            case CachedDecision.ModelOnlyVersion:
                return sources is null;
            case CachedDecision.CurrentVersion when sources is not null && sources.Length == slots.Length:
                // The slots were just read for this entry and nothing else holds them, so setting them in place is safe.
                for (var i = 0; i < slots.Length; i++)
                {
                    slots[i] = slots[i] with { Source = sources[i] };
                }

                return true;
            default:
                return false;
        }
    }

    private static bool HasMarkedSlot(AnswerSlot[] slots)
    {
        foreach (var slot in slots)
        {
            if (slot.Source != ProbabilitySource.Model)
            {
                return true;
            }
        }

        return false;
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
        public string? UpstreamProvider;
        public AnswerSlot[]? Slots;
        public ProbabilitySource[]? Sources;
        public double[]? Probabilities;
    }
}
