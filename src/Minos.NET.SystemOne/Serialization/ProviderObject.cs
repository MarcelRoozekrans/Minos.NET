using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minos.SystemOne.Serialization;

/// <summary>
/// A client's OpenRouter <c>provider</c> object, written once from its preferences, that a raw
/// <see cref="SystemOneRequest"/> carries to the wire. One instance per client, so a raw call allocates nothing more for it.
/// </summary>
internal sealed class ProviderObject(ReadOnlyMemory<byte> json)
{
    /// <summary>Gets the object, such as <c>{"zdr":true}</c>.</summary>
    public ReadOnlyMemory<byte> Json { get; } = json;
}

/// <summary>Writes a <see cref="ProviderObject"/> as it is. Requests are never read back, so reading skips the value.</summary>
internal sealed class ProviderObjectConverter : JsonConverter<ProviderObject>
{
    public override ProviderObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, ProviderObject value, JsonSerializerOptions options)
        => writer.WriteRawValue(value.Json.Span, skipInputValidation: true);
}
