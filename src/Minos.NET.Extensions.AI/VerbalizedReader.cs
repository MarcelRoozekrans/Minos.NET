using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI;

/// <summary>Asks for and reads the probabilities a model states for each label, as a JSON object keyed by label.</summary>
internal static class VerbalizedReader
{
    /// <summary>The name the response format gives its schema.</summary>
    internal const string SchemaName = "minos_answer";

    /// <summary>
    /// A JSON schema response format: an object with one number from 0 to 1 per label, every label required, and no
    /// other property. The schema is written by hand, so it needs no reflection.
    /// </summary>
    internal static ChatResponseFormat Format(int labelCount)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            for (var i = 0; i < labelCount; i++)
            {
                writer.WriteStartObject(LabelDistribution.Label(i));
                writer.WriteString("type", "number");
                writer.WriteNumber("minimum", 0);
                writer.WriteNumber("maximum", 1);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteStartArray("required");
            for (var i = 0; i < labelCount; i++)
            {
                writer.WriteStringValue(LabelDistribution.Label(i));
            }

            writer.WriteEndArray();
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }

        using var schema = JsonDocument.Parse(buffer.WrittenMemory);
        return ChatResponseFormat.ForJsonSchema(schema.RootElement.Clone(), SchemaName);
    }

    /// <summary>
    /// Each label's stated value, in label order. Other properties are ignored. Returns null when the text is not a JSON
    /// object, or a label is missing or not a number. The values are not checked; <see cref="LabelDistribution.FromStated"/>
    /// does that.
    /// </summary>
    internal static double[]? Read(string? text, int labelCount)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var stated = new double[labelCount];
            for (var i = 0; i < labelCount; i++)
            {
                if (!root.TryGetProperty(LabelDistribution.Label(i), out var value)
                    || value.ValueKind != JsonValueKind.Number
                    || !value.TryGetDouble(out stated[i]))
                {
                    return null;
                }
            }

            return stated;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
