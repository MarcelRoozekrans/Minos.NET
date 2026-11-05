using System.Buffers;
using System.Text.Json;

namespace Minos.Caching;

/// <summary>
/// The distributed-cache form of a <see cref="CachedDecision"/>: compact JSON with a version field. A slot is
/// <c>[valueIndex, value, confidence, offset]</c>. An entry with an answer whose probabilities are not the model's own
/// is version 2 and adds <c>"sources"</c>, one <see cref="ProbabilitySource"/> number per slot. An entry with an escalation path
/// is version 3 and adds <c>"path"</c>, with <c>"routes"</c> as <c>[answeredBy, reason, primaryCertainty, targetCertainty]</c>
/// per question, in definition order and without keys, and <c>"calls"</c> as
/// <c>[client, model, inputTokens, outputTokens, cost, isCached]</c>, plus the call's <see cref="CallOutcome"/> number and
/// its <see cref="DecisionErrorKind"/> number when it has one, but only for a call that did not answer; a version 3 entry still has <c>"sources"</c> when an
/// answer has a marked one. No reflection, so it is
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
            var version = value.Path is not null
                ? CachedDecision.PathVersion
                : marked ? CachedDecision.SourcesVersion : CachedDecision.ModelOnlyVersion;
            writer.WriteNumber("v"u8, version);
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
            if (value.Path is { } path)
            {
                WritePath(writer, path);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WritePath(Utf8JsonWriter writer, CachedPath path)
    {
        writer.WriteStartObject("path"u8);
        writer.WriteStartArray("routes"u8);
        foreach (var route in path.Routes)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(route.AnsweredBy);
            writer.WriteNumberValue((int)route.Reason);
            writer.WriteNumberValue(route.PrimaryCertainty);
            WriteNumberOrNull(writer, route.TargetCertainty);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("calls"u8);
        foreach (var call in path.Calls)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(call.ClientName);
            if (call.Model is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(call.Model);
            }

            WriteNumberOrNull(writer, call.Usage?.InputTokens);
            WriteNumberOrNull(writer, call.Usage?.OutputTokens);
            WriteNumberOrNull(writer, call.Usage?.Cost);
            writer.WriteBooleanValue(call.IsCached);
            if (call.Outcome != CallOutcome.Answered)
            {
                // Only a call that did not answer stores its outcome, and its error kind when it has one, so an entry
                // of answered calls keeps the shape earlier versions wrote and read.
                writer.WriteNumberValue((int)call.Outcome);
                if (call.ErrorKind is { } kind)
                {
                    writer.WriteNumberValue((int)kind);
                }
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
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
            || !TryApplySources(fields.Version, fields.Sources, fields.Slots)
            || (fields.Path is not null && fields.Version != CachedDecision.PathVersion))
        {
            return null;
        }

        return new CachedDecision(
            fields.Slots, fields.Probabilities, fields.Model, fields.InputTokens, fields.OutputTokens, fields.Cost, fields.Id, fields.UpstreamProvider, fields.Path);
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

        if (reader.ValueTextEquals("path"u8))
        {
            fields.Path = ReadPath(ref reader);
            return fields.Path is not null;
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

    // { "routes": [...], "calls": [...], ... }; null when the shape is wrong, a route or call could not be built, or a part is missing.
    private static CachedPath? ReadPath(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        CachedRoute[]? routes = null;
        DecisionCall[]? calls = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("routes"u8))
            {
                routes = ReadRoutes(ref reader);
                if (routes is null)
                {
                    return null;
                }
            }
            else if (reader.ValueTextEquals("calls"u8))
            {
                calls = ReadCalls(ref reader);
                if (calls is null)
                {
                    return null;
                }
            }
            else
            {
                // A property a later version added: skip its value.
                if (!reader.Read())
                {
                    return null;
                }

                reader.Skip();
            }
        }

        return reader.TokenType == JsonTokenType.EndObject && routes is not null && calls is { Length: > 0 } ? new CachedPath(routes, calls) : null;
    }

    // [[answeredBy, reason, primaryCertainty, targetCertainty|null], ...]; null when the shape is wrong or a value is one
    // that QuestionRoute would reject, so rebuilding the route later cannot throw.
    private static CachedRoute[]? ReadRoutes(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return null;
        }

        var routes = new List<CachedRoute>();
        while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
        {
            if (!TryReadRequiredString(ref reader, out var answeredBy)
                || !TryReadInt(ref reader, out var reason)
                || !Enum.IsDefined((RouteReason)reason)
                || !TryReadDouble(ref reader, out var primary)
                || !IsCertainty(primary)
                || !TryReadNullableDouble(ref reader, out var target)
                || (target is { } t && !IsCertainty(t))
                || !reader.Read()
                || reader.TokenType != JsonTokenType.EndArray)
            {
                return null;
            }

            routes.Add(new CachedRoute(answeredBy, (RouteReason)reason, primary, target));
        }

        return reader.TokenType == JsonTokenType.EndArray ? [.. routes] : null;
    }

    // [[client, model|null, input|null, output|null, cost|null, isCached, outcome?, errorKind?], ...]; null when the shape is wrong.
    private static DecisionCall[]? ReadCalls(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return null;
        }

        var calls = new List<DecisionCall>();
        while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
        {
            if (!TryReadRequiredString(ref reader, out var client)
                || !TryReadString(ref reader, out var model)
                || !TryReadNullableInt(ref reader, out var input)
                || input < 0
                || !TryReadNullableInt(ref reader, out var output)
                || output < 0
                || !TryReadNullableDouble(ref reader, out var cost)
                || !(cost is null or >= 0)
                || !reader.Read()
                || reader.TokenType is not (JsonTokenType.True or JsonTokenType.False))
            {
                return null;
            }

            var isCached = reader.GetBoolean();
            if (!TryReadOutcome(ref reader, out var outcome, out var errorKind))
            {
                return null;
            }

            // A call that reported no usage has none, as a response whose usage reports nothing has none.
            var usage = input is null && output is null && cost is null ? null : new DecisionUsage { InputTokens = input, OutputTokens = output, Cost = cost };
            calls.Add(new DecisionCall(client, model, usage, isCached) { Outcome = outcome, ErrorKind = errorKind });
        }

        return reader.TokenType == JsonTokenType.EndArray ? [.. calls] : null;
    }

    // The optional tail of a call, read after isCached up to the closing bracket: nothing for an answered call, else the
    // outcome and optionally the error kind. A value this version does not define makes the entry a miss.
    private static bool TryReadOutcome(ref Utf8JsonReader reader, out CallOutcome outcome, out DecisionErrorKind? errorKind)
    {
        outcome = CallOutcome.Answered;
        errorKind = null;
        if (!reader.Read())
        {
            return false;
        }

        if (reader.TokenType == JsonTokenType.EndArray)
        {
            return true;
        }

        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var outcomeNumber) || !Enum.IsDefined((CallOutcome)outcomeNumber))
        {
            return false;
        }

        outcome = (CallOutcome)outcomeNumber;
        if (!reader.Read())
        {
            return false;
        }

        if (reader.TokenType == JsonTokenType.EndArray)
        {
            return true;
        }

        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var kindNumber) || !Enum.IsDefined((DecisionErrorKind)kindNumber))
        {
            return false;
        }

        errorKind = (DecisionErrorKind)kindNumber;
        return reader.Read() && reader.TokenType == JsonTokenType.EndArray;
    }

    // The range QuestionRoute accepts; the negated check also rejects NaN.
    private static bool IsCertainty(double value) => value is >= 0 and <= 1;

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
            case CachedDecision.SourcesVersion when sources is not null && sources.Length == slots.Length:
            case CachedDecision.PathVersion when sources is not null && sources.Length == slots.Length:
                // The slots were just read for this entry and nothing else holds them, so setting them in place is safe.
                for (var i = 0; i < slots.Length; i++)
                {
                    slots[i] = slots[i] with { Source = sources[i] };
                }

                return true;
            case CachedDecision.PathVersion:
                // A version 3 entry has sources only when an answer has a marked one.
                return sources is null;
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

    // A number or null; false for any other token.
    private static bool TryReadNullableInt(ref Utf8JsonReader reader, out int? value)
    {
        value = null;
        if (!reader.Read())
        {
            return false;
        }

        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return true;
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                value = number;
                return true;
            default:
                return false;
        }
    }

    // A number or null; false for any other token.
    private static bool TryReadNullableDouble(ref Utf8JsonReader reader, out double? value)
    {
        value = null;
        if (!reader.Read())
        {
            return false;
        }

        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return true;
            case JsonTokenType.Number when reader.TryGetDouble(out var number):
                value = number;
                return true;
            default:
                return false;
        }
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

    // A string that is present and not empty, as the record reader requires of a client name; null is not a value here.
    private static bool TryReadRequiredString(ref Utf8JsonReader reader, out string value)
    {
        value = string.Empty;
        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
        {
            return false;
        }

        value = reader.GetString()!;
        return value.Length > 0;
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

    private static void WriteNumberOrNull(Utf8JsonWriter writer, int? value)
    {
        if (value is { } number)
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static void WriteNumberOrNull(Utf8JsonWriter writer, double? value)
    {
        if (value is { } number)
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteNullValue();
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
        public CachedPath? Path;
    }
}
