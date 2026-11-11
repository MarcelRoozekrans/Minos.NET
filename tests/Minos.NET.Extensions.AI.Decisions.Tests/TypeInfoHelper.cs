using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Minos.Extensions.AI.Decisions.Tests;

internal static class TypeInfoHelper
{
    // The generic overload is the one CA2263 asks for on net11.0; net10.0 only has the Type overload.
    public static JsonTypeInfo<T> GetTypeInfo<T>(JsonSerializerOptions options) =>
#if NET11_0_OR_GREATER
        options.GetTypeInfo<T>();
#else
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
#endif
}
