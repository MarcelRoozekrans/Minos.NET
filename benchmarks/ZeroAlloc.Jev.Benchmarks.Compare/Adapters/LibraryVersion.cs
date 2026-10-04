using System.Reflection;

namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>Reads a library's version from its assembly, as the result file reports it.</summary>
public static class LibraryVersion
{
    /// <summary>Gets the informational version of <paramref name="type"/>'s assembly, without a source-revision suffix.</summary>
    /// <param name="type">A type from the library.</param>
    /// <returns>The version, such as <c>0.2.0</c>.</returns>
    public static string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var assembly = type.Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is null)
        {
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
