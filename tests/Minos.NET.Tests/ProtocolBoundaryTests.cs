using System.Runtime.CompilerServices;

namespace Minos.Tests;

public sealed class ProtocolBoundaryTests
{
    // The /v1/systemone request envelope, question and answer field names. Only the protocol package's protocol may
    // name them.
    // Only the u8 literal form is matched, on purpose: plain strings would flag XML doc param names. A plain string or a
    // JsonPropertyName use of these names is not caught.
    private static readonly string[] WireNames =
    [
        "\"state\"u8", "\"model\"u8", "\"questions\"u8",
        "\"probabilities\"u8", "\"legend\"u8", "\"noul\"u8", "\"criteria\"u8", "\"instructions\"u8",
        "\"description\"u8", "\"examples\"u8", "\"not_for\"u8",
    ];

    private static readonly string[] Allowed =
    [
        "Protocols/",
    ];

    // The core package is provider-neutral: no file in it names a /v1/systemone field.
    [Fact]
    public void TheCoreNamesNoSystemOneWireField()
        => Assert.Empty(SourceFiles("Minos.NET").Where(f => WireNames.Any(n => f.Text.Contains(n, StringComparison.Ordinal))).Select(f => f.Path));

    [Fact]
    public void OnlyTheProtocolNamesTheSystemOneWireFields()
    {
        var offenders = SourceFiles("Minos.NET.SystemOne")
            .Where(f => !Allowed.Any(a => f.Path.StartsWith(a, StringComparison.Ordinal)))
            .Where(f => WireNames.Any(n => f.Text.Contains(n, StringComparison.Ordinal)))
            .Select(f => f.Path)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheCoreNamesNoEndpointPath()
        => Assert.Empty(SourceFiles("Minos.NET").Where(f => f.Text.Contains("\"v1/systemone\"", StringComparison.Ordinal)).Select(f => f.Path));

    // The resolved settings own the request path, per preset; every call, raw or neutral, takes it from them.
    [Fact]
    public void OnlyTheSettingsNameTheEndpointPath()
    {
        var offenders = SourceFiles("Minos.NET.SystemOne")
            .Where(f => !string.Equals(f.Path, "Transport/SystemOneClientSettings.cs", StringComparison.Ordinal))
            .Where(f => f.Text.Contains("\"v1/systemone\"", StringComparison.Ordinal))
            .Select(f => f.Path)
            .ToList();

        Assert.Empty(offenders);
    }

    // Every hand-written source file of one src project, by its path relative to the project.
    private static IEnumerable<(string Path, string Text)> SourceFiles(string project)
    {
        var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "src", project));
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText(f)));
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
