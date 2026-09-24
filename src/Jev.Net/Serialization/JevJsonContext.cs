using System.Text.Json.Serialization;

namespace Jev.Net.Serialization;

/// <summary>Source-generated, reflection-free serialization metadata for the Jev wire model.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(SystemOneRequest))]
internal sealed partial class JevJsonContext : JsonSerializerContext;
