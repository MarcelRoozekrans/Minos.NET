using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Jev.AotSmoke.Tests;

/// <summary>
/// A record member is excluded from the entry points only when the compiler generated it. These tests compile records
/// into an assembly and read it back as metadata, as the coverage test reads the packages, so they pin the rule
/// independently of what the packages happen to declare.
/// </summary>
public sealed class RecordMemberExclusionTests
{
    private const string Source = """
        public record Synthesized(int Value);

        public sealed record HandWritten(int Value)
        {
            public void Deconstruct(out int value) => value = Value;

            public bool Equals(HandWritten? other) => other is not null && Value == other.Value;

            public override int GetHashCode() => Value;
        }
        """;

    private static readonly IAssemblySymbol Records = Compile();

    private static readonly HashSet<string> Generated = PublicApi.FindGeneratedRecordMembers([Records]);

    [Theory]
    [InlineData("Synthesized", "<Clone>$")]
    [InlineData("Synthesized", "Deconstruct")]
    [InlineData("Synthesized", "PrintMembers")]
    [InlineData("Synthesized", "Equals")]
    public void A_member_the_compiler_generates_for_a_record_is_excluded(string type, string member)
        => Assert.All(Lines(type, member), line => Assert.NotNull(PublicApi.ExclusionReason(line, Generated)));

    [Theory]
    [InlineData("HandWritten", "Deconstruct")]
    [InlineData("HandWritten", "Equals")]
    public void A_record_member_its_author_wrote_is_an_entry_point(string type, string member)
    {
        // Equals(object?) is the runtime's contract and is excluded on any type; Equals(T? other) is the author's.
        var lines = Lines(type, member).Where(line => !line.Contains("Equals(object? obj)", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Null(PublicApi.ExclusionReason(line, Generated)));
    }

    private static List<string> Lines(string type, string member)
        => [.. Records.GetTypeByMetadataName(type)!.GetMembers(member).OfType<IMethodSymbol>().Select(ApiSignature.Of)];

    // Compiled and loaded back from its image, since the compiler adds [CompilerGenerated] when it emits.
    private static IAssemblySymbol Compile()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
        var records = CSharpCompilation.Create(
            "Records",
            [CSharpSyntaxTree.ParseText(Source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emitted = records.Emit(image);
        Assert.True(emitted.Success, string.Join('\n', emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        var reference = MetadataReference.CreateFromImage(image.ToArray());
        var reader = CSharpCompilation.Create("Reader", references: [.. references, reference]);
        return (IAssemblySymbol)reader.GetAssemblyOrModuleSymbol(reference)!;
    }
}
