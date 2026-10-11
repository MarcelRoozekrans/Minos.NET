using System.Runtime.CompilerServices;
using Minos.SystemOne.Protocols;

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
    public void TheSystemOneProtocolOwnsTheEndpointPath()
        => Assert.Equal("v1/systemone", SystemOneProtocol.Instance.EndpointPath);

    [Fact]
    public void TheCoreNamesNoEndpointPath()
        => Assert.Empty(SourceFiles("Minos.NET").Where(f => f.Text.Contains("\"v1/systemone\"", StringComparison.Ordinal)).Select(f => f.Path));

    // Transport/IDecisionApi.cs keeps the literal for its Post attribute: the raw call stays, and Phase 6.3 adds the neutral one.
    [Fact]
    public void OnlyTheProtocolAndTheApiInterfaceNameTheEndpointPath()
    {
        var offenders = SourceFiles("Minos.NET.SystemOne")
            .Where(f => !f.Path.StartsWith("Protocols/", StringComparison.Ordinal) && !string.Equals(f.Path, "Transport/IDecisionApi.cs", StringComparison.Ordinal))
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
