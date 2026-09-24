using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Net.Serialization;

/// <summary>
/// Reads a JSON string as text and any other JSON value as structured content; writes the reverse.
/// </summary>
internal sealed class JevContentConverter : JsonConverter<JevContent>
{
    public override JevContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => JevContent.FromString(reader.GetString()!),
            JsonTokenType.Null => throw new JsonException("Jev content cannot be null."),
            _ => JevContent.FromJson(JsonElement.ParseValue(ref reader)),
        };
    }

    public override void Write(Utf8JsonWriter writer, JevContent value, JsonSerializerOptions options)
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
