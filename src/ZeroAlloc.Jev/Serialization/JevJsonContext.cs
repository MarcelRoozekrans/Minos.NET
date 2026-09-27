using System.Text.Json.Serialization;

namespace ZeroAlloc.Jev.Serialization;

/// <summary>Source-generated, reflection-free serialization metadata for the Jev wire model.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(SystemOneRequest))]
[JsonSerializable(typeof(SystemOneResponse))]
[JsonSerializable(typeof(ModelList))]
internal sealed partial class JevJsonContext : JsonSerializerContext;
