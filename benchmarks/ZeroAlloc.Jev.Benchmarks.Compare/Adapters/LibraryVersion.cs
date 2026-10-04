using System.Reflection;

namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>Reads a library's version from its assembly, as the result file reports it.</summary>
public static class LibraryVersion
{
    // The commit is shortened to this many hex digits, as git's short form.
    private const int ShortCommitLength = 7;

    /// <summary>Gets the informational version of <paramref name="type"/>'s assembly, without a source-revision suffix.</summary>
    /// <param name="type">A type from a library taken from a package.</param>
    /// <returns>The version, such as <c>0.2.0</c>.</returns>
    public static string Of(Type type)
    {
        var (version, _) = Read(type);
        return version;
    }

    /// <summary>
    /// Gets the informational version of <paramref name="type"/>'s assembly with the short commit it was built from, for
    /// a library built from this repository's source rather than taken from a package.
    /// </summary>
    /// <param name="type">A type from a library built from source.</param>
    /// <returns>The version and commit, such as <c>0.3.3+1a2b3c4</c>, or the version alone when no commit is recorded.</returns>
    public static string OfSourceBuild(Type type)
    {
        var (version, commit) = Read(type);
        return commit is null ? version : version + "+" + commit[..Math.Min(commit.Length, ShortCommitLength)];
    }

    /// <summary>Splits an informational version into the version and the build metadata after <c>+</c>.</summary>
    /// <param name="informational">An informational version, such as <c>0.3.3+1a2b3c4d</c>.</param>
    /// <returns>The version, and the build metadata or <see langword="null"/> when there is none.</returns>
    internal static (string Version, string? Commit) Split(string informational)
    {
        ArgumentNullException.ThrowIfNull(informational);
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 || plus == informational.Length - 1
            ? (plus < 0 ? informational : informational[..plus], null)
            : (informational[..plus], informational[(plus + 1)..]);
    }

    private static (string Version, string? Commit) Read(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var assembly = type.Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational is null
            ? (assembly.GetName().Version?.ToString() ?? "unknown", null)
            : Split(informational);
    }
}
