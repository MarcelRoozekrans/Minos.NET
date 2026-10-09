using System.Text.Json;

namespace Minos.Serialization;

/// <summary>Serializer options for the Jev wire model, backed by <see cref="DecisionJsonContext"/>.</summary>
internal static class DecisionJson
{
    /// <summary>Gets options whose type-info resolver is the source-generated <see cref="DecisionJsonContext"/>.</summary>
    public static JsonSerializerOptions Options => DecisionJsonContext.Default.Options;
}
