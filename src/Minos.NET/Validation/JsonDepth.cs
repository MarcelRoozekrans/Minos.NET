using System.Text.Json;
using Minos.Generator;

namespace Minos.Validation;

/// <summary>JSON nesting depth: an object or array is one level, and a string, number or literal is none.</summary>
internal static class JsonDepth
{
    /// <summary>Returns the nesting depth: 0 for a string, number or literal; 1 for <c>{}</c> or <c>[]</c>.</summary>
    public static int Of(JsonElement element)
    {
        var deepest = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    deepest = Math.Max(deepest, Of(property.Value));
                }

                return deepest + 1;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    deepest = Math.Max(deepest, Of(item));
                }

                return deepest + 1;
            default:
                return 0;
        }
    }

    /// <summary>Whether <paramref name="content"/> is JSON nested deeper than <see cref="JevLimits.MaximumJsonDepth"/>.</summary>
    public static bool Exceeds(JevContent content) => content.TryGetJson(out var json) && Of(json) > JevLimits.MaximumJsonDepth;
}
