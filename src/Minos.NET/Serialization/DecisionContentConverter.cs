using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minos.Serialization;

/// <summary>
/// Reads a JSON string as text and any other JSON value as structured content; writes the reverse.
/// </summary>
internal sealed class DecisionContentConverter : JsonConverter<DecisionContent>
{
    public override DecisionContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return DecisionContent.FromString(reader.GetString()!);
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException("Jev content cannot be null.");
        }

        var element = JsonElement.ParseValue(ref reader);
        return element.ValueKind switch
        {
            JsonValueKind.Object or JsonValueKind.Array => DecisionContent.FromJson(element),
            _ => throw new JsonException("Jev content must be a string, object or array."),
        };
    }

    public override void Write(Utf8JsonWriter writer, DecisionContent value, JsonSerializerOptions options)
    {
        if (value.TryGetString(out var text))
        {
            writer.WriteStringValue(text);
        }
        else if (value.TryGetJson(out var json))
        {
            json.WriteTo(writer);
        }
        else
        {
            throw new InvalidOperationException("Cannot serialize uninitialized Jev content.");
        }
    }
}
