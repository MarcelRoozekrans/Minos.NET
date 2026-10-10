using System.Runtime.CompilerServices;

namespace Minos.Tests;

public sealed class ProtocolBoundaryTests
{
    // The /v1/systemone answer and question field names. Only the protocol, and the files Phase 6.3 moves behind it,
    // may name them.
    // Only the u8 literal form is matched, on purpose: plain strings would flag XML doc param names. A plain string or a
    // JsonPropertyName use of these names is not caught.
    private static readonly string[] WireNames = ["\"probabilities\"u8", "\"legend\"u8", "\"noul\"u8", "\"criteria\"u8", "\"instructions\"u8"];

    private static readonly string[] Allowed =
    [
        "Protocols/",
        "Serialization/QuestionsWriter.cs",
        "AnswerReader.cs",                 // removed in Task 7
    ];

    [Fact]
    public void OnlyTheProtocolNamesTheSystemOneWireFields()
    {
        var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "src", "Minos.NET"));
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(f => !Allowed.Any(a => f.Path.StartsWith(a, StringComparison.Ordinal)))
            .Where(f => WireNames.Any(n => f.Text.Contains(n, StringComparison.Ordinal)))
            .Select(f => f.Path)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
