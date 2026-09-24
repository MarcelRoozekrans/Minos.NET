using System.Text.Json;

namespace Jev.Net.Serialization;

/// <summary>Serializer options for the Jev wire model, backed by <see cref="JevJsonContext"/>.</summary>
internal static class JevJson
{
    /// <summary>Gets options whose type-info resolver is the source-generated <see cref="JevJsonContext"/>.</summary>
    public static JsonSerializerOptions Options => JevJsonContext.Default.Options;
}
