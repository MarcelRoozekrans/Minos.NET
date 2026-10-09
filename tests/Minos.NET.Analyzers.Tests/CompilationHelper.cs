using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Minos.Analyzers.Tests;

/// <summary>
/// Compiles a source string outside the analyzer-testing framework, for the tests that need to inspect a symbol
/// directly (a diagnostic's rendered message) or need a real, compiled-in-test assembly (an enum declared in a
/// referenced assembly rather than the source under test).
/// </summary>
internal static class CompilationHelper
{
    // The same reference assemblies AnalyzerVerifier resolves for the primary test compilation. Compiling this
    // helper's output against the live runtime's TRUSTED_PLATFORM_ASSEMBLIES instead would give it a corlib with a
    // different assembly identity than the ref-assembly corlib CSharpAnalyzerTest compiles the source under test
    // against, and a type shared between the two (an enum's base type, for one) would not bind as the same symbol.
    private static readonly Lazy<Task<ImmutableArray<MetadataReference>>> References = new(async () =>
        (await ReferenceAssemblies.Net.Net100.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .Add(MetadataReference.CreateFromFile(typeof(JevQuestionsAttribute).Assembly.Location)));

    public static Task<CSharpCompilation> CompileAsync(string source, string assemblyName = "CompilationHelperTest")
        => CompileAsync([CSharpSyntaxTree.ParseText(source, ParseOptions)], assemblyName);

    /// <summary>Compiles several documents together, for the tests that analyse one document of a compilation alone.</summary>
    public static async Task<CSharpCompilation> CompileAsync(IEnumerable<SyntaxTree> trees, string assemblyName = "CompilationHelperTest")
        => CSharpCompilation.Create(
            assemblyName,
            trees,
            await References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>Parses <paramref name="source"/> as the document at <paramref name="path"/>.</summary>
    public static SyntaxTree Parse(string source, string path) => CSharpSyntaxTree.ParseText(source, ParseOptions, path);

    private static CSharpParseOptions ParseOptions => new(LanguageVersion.Latest);

    /// <summary>Emits <paramref name="compilation"/> to an in-memory assembly, for use as another compilation's
    /// metadata reference (an enum declared "in a referenced assembly", whose findings land on the property that uses it).</summary>
    public static MetadataReference EmitToReference(this CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Failed to compile the test source:\n" + string.Join('\n', result.Diagnostics.Select(d => d.ToString())));
        }

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }
}
